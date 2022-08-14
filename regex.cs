using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text;
using System.Linq;
using System.IO;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO.Enumeration;

namespace regex;

/*
    SYNTAX:

        rsr s <directory> <pattern> [options]
        rsr r <directory> <pattern> <replacement> [options]

        options:
            c   case sensitive
            r   recursive
            s   short names
            d   only directories
            i   only files
            f   full path
            q   quiet
            1   first result
            x   execute/open file
//*/


public static class Program
{
    private static Process? _launcher;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathName([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder shortPath, int shortPathLength);





    public static int Main(string[] argv)
    {
        #region ARGUMENT PREPROCESSING

        Console.CancelKeyPress += delegate
        {
            "\n[cancelled]\n".Print(ConsoleColor.Red);

            _launcher?.Kill();
            _launcher?.Dispose();

            do
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Thread.Sleep(10);
            }
            while (Console.ForegroundColor != ConsoleColor.Gray);
        };
        //Console.ForegroundColor = ConsoleColor.Cyan;

        //for (int i = 0; i < argv.Length; i++)
        //    Console.WriteLine($"[{i}]: {argv[i]}");

        Console.ForegroundColor = ConsoleColor.White;

        if (argv.Length < 1)
            return "Not enugh arguments.\nUse `regex ?` for help".Print(ConsoleColor.Red, 1);

        string arguments = argv[0].SanitizeArguments();

        #endregion
        #region REGEX REPLACING

        char[] opt = { };

        if (arguments == "r")
            if (argv.Length < 4)
            {
                "Not enugh arguments.".Print(ConsoleColor.Red);

                return "Use `regex ?` for help".Print(ConsoleColor.Yellow, 1);
            }
            else
            {
                if (argv.Length > 4)
                    opt = argv[4].SanitizeArguments().ToCharArray();

                if (!Directory.Exists(argv[1]))
                    return $"The directory \'{argv[1]}\' does not exist.".Print(ConsoleColor.Red, 1);

                try
                {
                    FilterMode mode = opt.Contains('d') ? FilterMode.DirsOnly : opt.Contains('i') ? FilterMode.FilesOnly : FilterMode.All;
                    RegexOptions ropt = (opt.Contains('c') ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.Compiled;
                    bool short_names = opt.Contains('s');
                    bool full_path = opt.Contains('f');
                    bool recursive = opt.Contains('r');
                    bool details = opt.Contains('l');
                    bool quiet = opt.Contains('q');
                    bool single = opt.Contains('1');

                    if (opt.Contains('n') && !quiet)
                        "The option 'N' is invalid for a replacement operation. It will therefore be ignored.".Print(ConsoleColor.Yellow);

                    Regex regex = new Regex(argv[2], ropt);
                    int count = 0;

                    foreach ((FileSystemInfo info, string replacement) in from old in Enumerate(argv[1], recursive)
                                                                          let path = full_path ? old.FullName : old.Name
                                                                          where regex.IsMatch(path)
                                                                          let repl = regex.Replace(path, argv[3])
                                                                          select (old, repl))
                        try
                        {
                            ++count;

                            string displ = $"{(short_names ? GetShortPath(info.FullName) : info.FullName)}  --->  {(short_names ? GetShortPath(replacement) : replacement)}";

                            if (details && !quiet)
                                displ = $"{info.DetailedPrefix()} {displ}";

                            if (info is DirectoryInfo dir)
                                dir.MoveTo(replacement);
                            else if (info is FileInfo file)
                                file.MoveTo(replacement);

                            displ.Print(ConsoleColor.Green);

                            if (single)
                                break;
                        }
                        catch (Exception ex)
                        {
                            ex.Message.Print(ConsoleColor.Red);
                        }

                    if (!quiet)
                        $"\n {count} matches".Print(ConsoleColor.Gray);

                    return 0;
                }
                catch (ArgumentException)
                {
                    return $"Invalid Regex pattern:\n\t{argv[2]}".Print(ConsoleColor.Red, 1);
                }
                catch (Exception ex)
                {
                    return $"An error occured:\n\t{ex.Message}".Print(ConsoleColor.Red, 1);
                }
            }

        #endregion
        #region REGEX SEARCH

        if (arguments == "s")
            if (argv.Length < 3)
                return "Not enugh arguments.\nUse `regex ?` for help".Print(ConsoleColor.Red, 1);
            else
            {
                if (argv.Length > 3)
                    opt = argv[3].SanitizeArguments().ToCharArray();
                
                if (!Directory.Exists(argv[1]))
                    return $"The directory \'{argv[1]}\' does not exist.".Print(ConsoleColor.Red, 1);

                try
                {
                    RegexOptions ropt = (opt.Contains('c') ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.Compiled;
                    FilterMode mode = opt.Contains('d') ? FilterMode.DirsOnly : opt.Contains('i') ? FilterMode.FilesOnly : FilterMode.All;
                    bool not_match = opt.Contains('n');
                    bool short_names = opt.Contains('s');
                    bool full_path = opt.Contains('f');
                    bool recursive = opt.Contains('r');
                    bool details = opt.Contains('l');
                    bool quiet = opt.Contains('q');
                    bool single = opt.Contains('1');
                    bool exec = opt.Contains('x');
                    int count = 0;

                    single |= exec;

                    foreach (FileSystemInfo nfo in from old in Enumerate(argv[1], recursive)
                                                   let path = full_path ? old.FullName : old.Name
                                                   where Regex.IsMatch(path, argv[2], ropt) ^ not_match
                                                   select old)
                        if ((mode == FilterMode.DirsOnly && nfo is DirectoryInfo) ||
                            (mode == FilterMode.FilesOnly && nfo is FileInfo) ||
                            mode == FilterMode.All)
                        {
                            ++count;

                            string name = short_names ? GetShortPath(nfo.FullName) : nfo.FullName;

                            if (details)
                                name = $"{nfo.DetailedPrefix()} {name}";

                            if (single)
                            {
                                (details ? name : $"\"{name}\"").Print(ConsoleColor.Green);

                                if (exec)
                                    using (_launcher = new Process
                                    {
                                        StartInfo = new ProcessStartInfo
                                        {
                                            UseShellExecute = true,
                                            FileName = nfo.FullName,
                                        }
                                    })
                                    {
                                        _launcher.Start();
                                        _launcher.WaitForExit();
                                    }

                                break;
                            }
                            else
                                name.Print(ConsoleColor.Green);
                        }

                    if (!quiet && !single)
                        $"\n {count} matches".Print(ConsoleColor.Gray);
                }
                catch (ArgumentException)
                {
                    return $"Invalid Regex pattern:\n\t{argv[2]}".Print(ConsoleColor.Red, 1);
                }
                catch (Exception ex)
                {
                    return $"An error occured:\n\t{ex.Message}".Print(ConsoleColor.Red, 1);
                }

                return 0;
            }

        #endregion
        #region HELP MENU

        if (arguments == "?" || arguments == "h")
            return @"
.--------------------------------------------.
|  REGULAR EXPRESSION FILE INTERACTION TOOL  |
|             - by Unknown6656 -             |
'--------------------------------------------'

usage:
    regex ?
    regex s <directory> <regular expression> [options]
    regex r <directory> <regular expression> <replacement> [options]

switch:
    ? > shows the current help menu
    s > performs a search
    r > performs a replacement
directory:
        A valid directory, which can also be relative.
        Use `.` for the current one and `..` for the parent dir.
regular expression:
        A regex-string to match the input file names
replacement:
        A replacement string which uses `$` to call groups captured with `(` and `)`.
[options]:
    c > forces case sensitiveness
    s > displays the short file names
    r > performs a recursive search or replacement
    d > only filter for directories
    i > only filter for files
    n > do NOT match the sequence (only possible with a search-expression)
    l > detailed file list
    f > search or replace the full absolute file name instead of the relative one
    q > a quiet output
    1 > limit search/replacement to a single result
    x > execute/open file (search only, implies ""1"")
    

examples:
    regex s ./MyFolder ""DSC([0-9]{4})\.(jpg|png)""
    regex r C:\Folder1\Folder2 ""([0-9a-fA-F])\s-\s(.*)\.mp3"" ""$2 - $1.mp3"" -cr
    regex s %windir%/system32 calc x
".Print(ConsoleColor.Yellow);

        #endregion

        return $"Unknown switch `{arguments}`.\nUse `regex ?` for help".Print(ConsoleColor.Red, 1);
    }

    public static IEnumerable<FileSystemInfo> Enumerate(string path, bool recursive)
    {
        IEnumerable<FileSystemInfo> enum_dir(DirectoryInfo dir)
        {
            foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo sub && recursive)
                    enum_dir(sub);

                yield return entry;
            }
        }

        return enum_dir(new DirectoryInfo(path));
    }

    public static string DetailedPrefix(this FileSystemInfo nfo) => $"[{nfo.CreationTime:yyyy-MM-dd,HH:ff:ss}] [{nfo.LastWriteTime:yyyy-MM-dd,HH:ff:ss}] {(int)nfo.Attributes:x8} ";

    public static int Print(this string msg, ConsoleColor color, int ret = 0)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = ConsoleColor.Gray;

        return ret;
    }

    public static string GetShortPath(string path)
    {
        StringBuilder sb = new StringBuilder(260);

        GetShortPathName(path, sb, sb.Capacity);

        return sb.ToString();
    }

    public static string SanitizeArguments(this string inp) => inp.Replace("-", "")
                                                                  .Replace("/", "")
                                                                  .Replace("\\", "")
                                                                  .ToLower()
                                                                  .Trim();
}

enum FilterMode
{
    All,
    DirsOnly,
    FilesOnly,
}
