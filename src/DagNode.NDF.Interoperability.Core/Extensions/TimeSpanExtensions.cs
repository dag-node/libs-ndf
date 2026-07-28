namespace DagNode.NDF.Interoperability;

public static class TimeSpanExtensions
{
    public enum UnitType
    {
	    /// <summary>
	    /// Short units (d, h, m, s, ms)
	    /// </summary>
        Compact,
        /// <summary>
        /// Long units (day/days, hour/hours, etc.)
        /// </summary>
        Abbreviated
    }

    public static string ToReadable(this TimeSpan ts, UnitType unitType = UnitType.Compact)
    {
        Span<char> buffer = stackalloc char[64]; // Preallocate buffer to avoid heap allocations
        var writer = new ValueStringBuilder(buffer);

        if (unitType == UnitType.Abbreviated) {
	        AppendAbbreviatedUnit(writer, ts.Days, day, days);
	        AppendAbbreviatedUnit(writer, ts.Hours, hour, hours);
	        AppendAbbreviatedUnit(writer, ts.Minutes, minute, minutes);
	        AppendAbbreviatedUnit(writer, ts.Seconds, second, seconds);
	        AppendAbbreviatedUnit(writer, ts.Milliseconds, millisecond, milliseconds);
        } else {
	        AppendCompactUnit(writer, ts.Days, d);
	        AppendCompactUnit(writer, ts.Hours, h);
	        AppendCompactUnit(writer, ts.Minutes, m);
	        AppendCompactUnit(writer, ts.Seconds, s);
	        AppendCompactUnit(writer, ts.Milliseconds, ms);
        }

        // Trim trailing space if present
        return writer.ToString().TrimEnd();

        void AppendCompactUnit(ValueStringBuilder writer, int value, string shortUnit)
        {
	        if (value <= 0) return;
	        writer.Append(shortUnit);
	        writer.Append(' '); // Add space to separate units
        }

        void AppendAbbreviatedUnit(ValueStringBuilder writer, int value, string longSingular, string longPlural)
        {
	        if (value <= 0) return;
	        writer.Append(value.ToString());
	        writer.Append(' ');
	        writer.Append(value == 1 ? longSingular : longPlural);
	        writer.Append(' '); // Add space to separate units
        }
    }

    private const string d = "d";
    private const string h = "h";
    private const string m = "m";
    private const string s = "s";
    private const string ms = "ms";

    private const string day = "day";
    private const string hour = "hour";
    private const string minute = "minute";
    private const string second = "second";
    private const string millisecond = "millisecond";
    
    private const string days = "days";
    private const string hours = "hours";
    private const string minutes = "minutes";
    private const string seconds = "seconds";
    private const string milliseconds = "milliseconds";

    private ref struct ValueStringBuilder(Span<char> initialBuffer)
    {
        private readonly Span<char> _buffer = initialBuffer;
        private int _pos = 0;

        public void Append(char c)
	        => _buffer[_pos++] = _pos < _buffer.Length
		        ? c : throw new InvalidOperationException("Buffer size exceeded");

        public void Append(string s)
        {
	        for (int index = 0; index < s.Length; index++) {
		        char c = s[index];
		        Append(c);
	        }
        }

        public override string ToString()
	        => new(_buffer.Slice(0, _pos));
    }
}
