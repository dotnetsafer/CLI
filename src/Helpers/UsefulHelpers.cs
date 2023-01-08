﻿using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bytehide.CLI.Helpers
{
    public static class UsefulHelpers
    {
        public static Process OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // Works ok on windows
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Process.Start("xdg-open", url); // Works ok on linux
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Process.Start("open", url); // Not tested
            return null;
        }
    }
}
