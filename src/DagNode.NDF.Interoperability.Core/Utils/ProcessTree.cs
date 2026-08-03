using System.Globalization;
using System.Runtime.InteropServices;

namespace DagNode.NDF.Interoperability;

/// <summary>
/// Terminates a process and its descendants by signalling PIDs directly, discovering the subtree from
/// <c>/proc/&lt;pid&gt;/stat</c> parent links. Uses no external process — not <c>pgrep</c>, <c>ps</c>
/// or the <c>kill</c> binary, any of which a sandbox may forbid. This is the <c>netstandard2.1</c>
/// substitute for <c>Process.Kill(entireProcessTree)</c>, which exists only on .NET Core 3.0+ and would
/// in any case orphan bash when the tracked process is the <c>setsid</c> wrapper.
///
/// Thread-safe: the type holds no mutable state (bar the one-time capability probe), every method works
/// on locals, and the signalling syscalls are reentrant, so concurrent terminations of different
/// subtrees do not interfere.
///
/// PID reuse — a target exiting and its PID being reused between discovery and signalling — is handled
/// on two levels:
/// <list type="bullet">
/// <item>Design goal: <c>pidfd</c> (Linux 5.3+). An fd from <c>pidfd_open</c> pins the exact process, so
/// <c>pidfd_send_signal</c> can never hit a reused PID. Used whenever the glibc wrappers are present
/// (RHEL 10 / glibc 2.36+); detected once and preferred.</item>
/// <item>Portable fallback: capture each process' start time (<c>/proc/&lt;pid&gt;/stat</c> field 22,
/// unique per PID for that process' lifetime) during the walk and re-check it immediately before the
/// <c>kill(2)</c>; a PID with a different start time is skipped. Residual window is the microseconds
/// between that re-check and the syscall.</item>
/// </list>
/// Callers narrow the window further by skipping termination once the call's pidfile is gone (it
/// finished); the SIGKILL pass re-reads the subtree so a process that exited on SIGTERM is not
/// signalled again.
/// </summary>
internal static class ProcessTree
{
	private const int SIGTERM = 15;
	private const int SIGKILL = 9;

	// In /proc/<pid>/stat, fields 1 (pid) and 2 (comm) precede the last ')'; splitting the text after it
	// yields field 3 at index 0. ppid is field 4 (index 1); starttime is field 22 (index 19).
	private const int PpidIndexAfterComm = 1;
	private const int StartTimeIndexAfterComm = 19;

	[DllImport("libc", SetLastError = true)]
	private static extern int kill(int pid, int sig);

	#region pidfd (preferred) — glibc 2.36+ wrappers, arch-safe (no hardcoded syscall numbers)

	[DllImport("libc", SetLastError = true)] private static extern int pidfd_open(int pid, uint flags);
	[DllImport("libc", SetLastError = true)] private static extern int pidfd_send_signal(int pidfd, int sig, IntPtr info, uint flags);
	[DllImport("libc", SetLastError = true)] private static extern int close(int fd);
	[DllImport("libc", SetLastError = true)] private static extern int getpid();

	// Probed once: true when the pidfd_open wrapper is present and functional on this host.
	private static readonly bool s_pidfdAvailable = ProbePidfd();

	private static bool ProbePidfd()
	{
		try {
			int fd = pidfd_open(getpid(), 0);
			if (fd < 0) return false;
			close(fd);
			return true;
		} catch (Exception) {
			return false; // EntryPointNotFound on older glibc, or DllNotFound: fall back to /proc + kill(2).
		}
	}

	#endregion

	/// <summary>
	/// Gracefully terminates the subtree rooted at <paramref name="rootPid"/>: SIGTERM (deepest first),
	/// wait <paramref name="grace"/> so each process' own handlers can run, then SIGKILL whatever
	/// survives. The subtree is re-read for the SIGKILL pass, so processes that exited on SIGTERM are
	/// not signalled again and any late children are still caught.
	/// </summary>
	public static async Task TerminateTreeAsync(int rootPid, TimeSpan grace, CancellationToken cancellationToken = default)
	{
		Signal(CollectSubtree(rootPid), SIGTERM);
		try {
			await Task.Delay(grace, cancellationToken).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Cancelled grace still escalates to SIGKILL below — the tree is being torn down regardless.
		}
		Signal(CollectSubtree(rootPid), SIGKILL);
	}

	/// <summary>SIGKILLs the whole subtree rooted at <paramref name="rootPid"/> at once, for synchronous teardown.</summary>
	public static void KillTree(int rootPid) => Signal(CollectSubtree(rootPid), SIGKILL);

	// Deepest first: signal children before the parent so a parent cannot briefly re-observe a live child.
	private static void Signal(IReadOnlyList<(int Pid, ulong StartTime)> subtreeBreadthFirst, int sig)
	{
		for (int i = subtreeBreadthFirst.Count - 1; i >= 0; i--) {
			(int pid, ulong expectedStartTime) = subtreeBreadthFirst[i];
			SignalOne(pid, expectedStartTime, sig);
		}
	}

	private static void SignalOne(int pid, ulong expectedStartTime, int sig)
	{
		if (s_pidfdAvailable) {
			int fd = pidfd_open(pid, 0);
			if (fd < 0) return; // Process already gone.
			try {
				// The fd pins whichever process held pid at open time; confirm it is still ours (a PID
				// reused just before pidfd_open has a different start time) before signalling through it.
				if (TryReadProc(pid, out _, out ulong currentStartTime) && currentStartTime == expectedStartTime) {
					pidfd_send_signal(fd, sig, IntPtr.Zero, 0);
				}
			} finally {
				close(fd);
			}
			return;
		}
		// Fallback: re-validate start time immediately before kill(2) to avoid signalling a reused PID.
		if (TryReadProc(pid, out _, out ulong startTime) && startTime == expectedStartTime) kill(pid, sig);
	}

	// Breadth-first walk of the ppid graph from rootPid; index 0 is the root, later entries are deeper.
	// Carries each PID's start time so Signal can detect reuse before killing.
	private static List<(int Pid, ulong StartTime)> CollectSubtree(int rootPid)
	{
		Dictionary<int, List<int>> childrenByParent = ReadProcSnapshot(out Dictionary<int, ulong> startTimeByPid);
		if (!startTimeByPid.TryGetValue(rootPid, out ulong rootStartTime)) return new List<(int, ulong)>(); // Root already gone
		var ordered = new List<(int, ulong)> { (rootPid, rootStartTime) };
		var seen = new HashSet<int> { rootPid };
		var queue = new Queue<int>();
		queue.Enqueue(rootPid);
		while (queue.Count > 0) {
			int pid = queue.Dequeue();
			if (!childrenByParent.TryGetValue(pid, out List<int>? children)) continue;
			foreach (int child in children) {
				if (seen.Add(child) && startTimeByPid.TryGetValue(child, out ulong childStartTime)) {
					queue.Enqueue(child);
					ordered.Add((child, childStartTime));
				}
			}
		}
		return ordered;
	}

	private static Dictionary<int, List<int>> ReadProcSnapshot(out Dictionary<int, ulong> startTimeByPid)
	{
		var childrenByParent = new Dictionary<int, List<int>>();
		startTimeByPid = new Dictionary<int, ulong>();
		foreach (string dir in Directory.EnumerateDirectories("/proc")) {
			if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue; // Skip /proc/self, /proc/net, ...
			if (!TryReadProc(pid, out int ppid, out ulong startTime)) continue;
			startTimeByPid[pid] = startTime;
			if (ppid <= 0) continue;
			if (!childrenByParent.TryGetValue(ppid, out List<int>? children)) {
				children = new List<int>();
				childrenByParent[ppid] = children;
			}
			children.Add(pid);
		}
		return childrenByParent;
	}

	private static bool TryReadProc(int pid, out int ppid, out ulong startTime)
	{
		ppid = -1;
		startTime = 0;
		try {
			// /proc/<pid>/stat: "pid (comm) state ppid ... starttime ..." — comm can hold spaces and
			// parens, so read the fields after the last ')'.
			string stat = File.ReadAllText($"/proc/{pid}/stat");
			int lastParen = stat.LastIndexOf(')');
			if (lastParen < 0 || lastParen + 2 >= stat.Length) return false;
			string[] fields = stat.Substring(lastParen + 2).Split(' ');
			if (fields.Length <= StartTimeIndexAfterComm) return false;
			return int.TryParse(fields[PpidIndexAfterComm], NumberStyles.Integer, CultureInfo.InvariantCulture, out ppid)
				& ulong.TryParse(fields[StartTimeIndexAfterComm], NumberStyles.Integer, CultureInfo.InvariantCulture, out startTime);
		} catch (Exception) {
			return false; // The process vanished mid-walk, or /proc entry is unreadable: not our subtree to signal.
		}
	}
}
