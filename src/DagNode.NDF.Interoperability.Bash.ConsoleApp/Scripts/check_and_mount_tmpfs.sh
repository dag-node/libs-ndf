#!/bin/bash
# Ensure /tmp/tmpfs-bs directory is created and mounted as tmpfs
#
# 	Creates the directory if /tmp is mounted as tmpfs already
#	Mounts /tmp/tmpfs-bs as tmpfs if /tmp is not tmpfs
check_and_mount_tmpfs() {
    local mount_point="/tmp/tmpfs-bs"

    # Check if /tmp is already a tmpfs
    if mount | grep -qE "^tmpfs on /tmp "; then
        # echo "/tmp is already a tmpfs"
        # Ensure the subdirectory exists
        if [ ! -d "${mount_point}" ]; then
            mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }
        fi
        echo "TMPFS_CREATE_SUCCESS"
    else
        # echo "/tmp is not a tmpfs"
        # Ensure the subdirectory exists
        if [ ! -d "${mount_point}" ]; then
            mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }
        fi
        # Attempt to mount a tmpfs to the subdirectory
        if mount -t tmpfs -o size=64M tmpfs "${mount_point}"; then
            echo "TMPFS_CREATE_SUCCESS"
        else
            echo "TMPFS_CREATE_ERROR"
            return 1
        fi
    fi
}

check_and_mount_tmpfs() {
    local mount_point="/tmp/tmpfs-bs"
    if mount | grep -qE "^tmpfs on /tmp "; then
        if [ ! -d "${mount_point}" ]; then
            mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }
        fi
        echo "TMPFS_CREATE_SUCCESS"
    else
        if [ ! -d "${mount_point}" ]; then
            mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }
        fi
        if mount -t tmpfs -o size=64M tmpfs "${mount_point}"; then
            echo "TMPFS_CREATE_SUCCESS"
        else
            echo "TMPFS_CREATE_ERROR"
            return 1
        fi
    fi
}
# Not using this file, inlined directly in LinuxUtils.cs
# CheckAndMountTmpfsBsInline