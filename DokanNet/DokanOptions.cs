﻿using System;

namespace DokanNet
{
    [Flags]
    public enum DokanOptions : long
    {
        DebugMode = 1, // ouput debug message
        StderrOutput = 2, // ouput debug message to stderr
        AltStream = 4, // use alternate stream
        NetworkDrive = 16, // use network drive, you need to install Dokan network provider.
        RemovableDrive = 32, // use removable drive
        FixedDrive = 0,
    }
}