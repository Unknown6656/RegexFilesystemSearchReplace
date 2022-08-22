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
using CommandLine;
using System.Security.Cryptography;
using CommandLine.Text;

namespace rsr;

/*
    SYNTAX:

        rsr s <directory> <pattern> [options]
        rsr r <directory> <pattern> <replacement> [options]

        options:
            c   case sensitive
            r   recursive search
            s   display short names
            l   detailled file list
            d   list only directories
            f   list only files
            u   search full path
            q   quiet
            i   invert results (non-matching)
            1   first result
            x   execute/open results

        replacement:
            $xxxx       refers to group xxxx
            $$          escapes to      $
            $created    refers to the created date
            $modified   refers to the modification date
            $accessed   refers to the accessed date
            $size       refers to the file size in bytes
            $hsize      refers to the human readable file size (with units)





//////////////////// the following is the old help text



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
    m > replace only the match with replacement instead of the entire file name with replacement
    o > force overwrite on replacement
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

//*/



public abstract class CLI_Base
{
    [Option('c', HelpText = "Case sensitive search")]
    public bool CaseSensitive { get; set; }

    [Option('q')]
    public bool Quiet { get; set; }

    [Option('r')]
    public bool Recursive { get; set; }

    // Computer\HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem\NtfsDisable8dot3NameCreation set to 0
    [Option('s')]
    public bool ShortNames { get; set; }

    [Option('l')]
    public bool DetailedFileList { get; set; }

    [Option('d', SetName = "only_dirs")]
    public bool OnlyDirectories { get; set; }

    [Option('f', SetName = "only_files")]
    public bool OnlyFiles { get; set; }

    [Option('u')]
    public bool FullPath { get; set; }

    [Option('n', "count", HelpText = "Limits to the first n results.", Default = -1)]
    public int Count { get; set; } = -1;

    public bool IsReplacement => this is CLI_Replace;

    public abstract string Directory { get; set; }

    public abstract string SearchPattern { get; set; }
}

[Verb("s", HelpText = "Searches for file system entries matching a given pattern.")]
public sealed class CLI_Search
    : CLI_Base
{
    [Value(0, Required = true, HelpText = "The working directory (default value is '.').", MetaName = "directory")]
    public override string Directory { get; set; } = ".";

    [Value(1, Required = true, HelpText = "The regex search pattern.", MetaName = "pattern")]
    public override string SearchPattern { get; set; } = "";

    [Option('i')]
    public bool InvertResults { get; set; }

    [Option('x')]
    public bool Execute { get; set; }
}

[Verb("r", HelpText = "Renames/Moves file system entries based on a given pattern.")]
public sealed class CLI_Replace
    : CLI_Base
{
    [Value(0, Required = true, HelpText = "The working directory (default value is '.').", MetaName = "directory")]
    public override string Directory { get; set; } = ".";

    [Value(1, Required = true, HelpText = "The regex search pattern.", MetaName = "pattern")]
    public override string SearchPattern { get; set; } = "";

    [Value(2, Required = true, HelpText = "The replacement string.", MetaName = "replacement")]
    public string Replacement { get; set; } = "";


    [Option('k')]
    public bool MockReplacement { get; set; }

    [Option('m')]
    public bool ReplaceMatchOnly { get; set; }

    [Option('o')]
    public bool ForceOverwrite { get; set; }
}


public static class Program
{
    private static Process? _launcher;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetShortPathName([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder shortPath, int shortPathLength);


    private static int Main(string[] argv)
    {
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

        int return_code;

        try
        {
            ParserResult<object> result = new Parser(config =>
            {
                config.HelpWriter = null; // Console.Out;
                config.AutoVersion = false;
            }).ParseArguments<CLI_Search, CLI_Replace>(argv);

            return_code = result.MapResult<CLI_Search, CLI_Replace, int>(MainSearch, MainReplace, errors =>
            {
                HelpText help = HelpText.AutoBuild(result, h =>
                {
                    h.AdditionalNewLineAfterOption = false;
                    h.Heading += " - Regex Filesystem Search and Replace";
                    h.Copyright = $"Copyright (c) 2016-{DateTime.UtcNow:yyyy}, unknown6656";
                    h.AddDashesToOption = true;
                    h.AddEnumValuesToHelpText = true;
                    h.AddNewLineBetweenHelpSections = true;
                    h.AutoVersion = false;
                    h.AddVerbs(typeof(CLI_Search), typeof(CLI_Replace));

                    return HelpText.DefaultParsingErrorsHandler(result, h);
                }, e => e, verbsIndex: true);

                Console.WriteLine(help);

                return -1;
            });
        }
        catch (Exception ex)
        {
            return_code = ex.HResult;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex);
        }

        Console.ForegroundColor = ConsoleColor.Gray;

        return return_code;
    }

    private static int MainCommon(CLI_Base arguments, out DirectoryInfo dir, out (FileSystemInfo entry, Match match, string path, string display)[] entries)
    {
        if (!arguments.Quiet)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Raw command line arguments:");

            int i = 0;

            foreach (string arg in Environment.GetCommandLineArgs())
                Console.WriteLine($"   {i++,2}: {arg}");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        dir = new(arguments.Directory);
        entries = Array.Empty<(FileSystemInfo, Match, string, string)>();

        Regex regex = new(arguments.SearchPattern, RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.NonBacktracking | (arguments.CaseSensitive ? default : RegexOptions.IgnoreCase));

        if (!dir.Exists)
            return $"The directory \'{arguments.Directory}\' does not exist.".Print(ConsoleColor.Red, -1);
        else if (arguments is CLI_Search { Count: not 1, Execute: true })
            return "The option to execute/open a file system entry is currently only available if the result limit has been set to 1.".Print(ConsoleColor.Red, -1);

        IEnumerable<(FileSystemInfo, Match, string, string)> results = from entry in Enumerate(dir, arguments.Recursive)
                                                                       let path = arguments.FullPath ? entry.FullName : entry.Name
                                                                       let match = regex.Match(path)
                                                                       where match.Success ^ ((arguments as CLI_Search)?.InvertResults ?? false)
                                                                       where (arguments.OnlyDirectories, arguments.OnlyFiles, entry) switch
                                                                       {
                                                                           (true, false, DirectoryInfo) => true,
                                                                           (false, true, FileInfo) => true,
                                                                           (false, false, _) => true,
                                                                           _ => false,
                                                                       }
                                                                       let display = entry.FullName.GetPath(arguments)
                                                                       select (entry, match, path, display + (entry is DirectoryInfo ? "\\" + ""));

        if (arguments.Count >= 0)
            results = results.Take(arguments.Count);

        entries = results.ToArray();

        int maxdisplaylength = entries.Max(e => e.display.Length);

        for (int i = 0; i < entries.Length; ++i)
            entries[i].display = entries[i].display.PadRight(maxdisplaylength);

        return 0;
    }

    private static int MainSearch(CLI_Search arguments)
    {
        if (MainCommon(arguments, out DirectoryInfo dir, out (FileSystemInfo, Match, string, string)[] entries) is not 0 and int retcode)
            return retcode;
        else if (!arguments.Quiet && entries.Length > 20)
            $"{entries.Length} matches found:".Print(ConsoleColor.White);

        foreach ((FileSystemInfo entry, _, _, string display) in entries)
        {
            (arguments.DetailedFileList ? $"{entry.DetailedPrefix()} {display}" : display).Print(ConsoleColor.Green);

            if (arguments.Execute)
                using (_launcher = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = entry.FullName,
                    }
                })
                {
                    _launcher.Start();
                    _launcher.WaitForExit();

                    break;
                }
        }

        if (!arguments.Quiet)
            $"{(entries.Length is int count and > 0 ? count.ToString() : "No")} matches found in '{dir.FullName}'.".Print(entries.Length > 0 ? ConsoleColor.White : ConsoleColor.Yellow);

        return 0;
    }

    private static int MainReplace(CLI_Replace arguments)
    {
        if (MainCommon(arguments, out DirectoryInfo dir, out (FileSystemInfo, Match, string, string)[] entries) is not 0 and int retcode)
            return retcode;
        else if (!arguments.Quiet && entries.Length > 3)
            $"{entries.Length} matches found:".Print(ConsoleColor.White);

        int skipped = 0;

        foreach ((FileSystemInfo entry, Match match, string path, string display) in entries)
            try
            {
                const string DOLLAR_ESCAPE = "<:$:>";
                string replacement = arguments.Replacement.Replace("$$", DOLLAR_ESCAPE)
                                                          .Replace("$created", entry.CreationTime.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the created date
                                                          .Replace("$ucreated", entry.CreationTimeUtc.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the created date
                                                          .Replace("$modified", entry.LastWriteTime.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the modification date
                                                          .Replace("$umodified", entry.LastWriteTimeUtc.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the modification date
                                                          .Replace("$accessed", entry.LastAccessTime.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the accessed date
                                                          .Replace("$uaccessed", entry.LastAccessTimeUtc.ToString("yyyy-MM-dd HH-mm-ss")) // refers to the accessed date
                                                          .Replace("$size", (entry as FileInfo)?.Length.ToString()) // refers to the file size in bytes
                                                          .Replace("$hsize", (entry as FileInfo)?.Length.ToHumanReadable() ?? "0B"); // refers to the human readable file size(with units)
                int index = 0;

                do
                {
                    index = replacement.IndexOf('$', index);

                    if (index < 0 || index >= replacement.Length)
                        break;

                    if (new string(replacement.Skip(index + 1).TakeWhile(char.IsNumber).ToArray()) is { Length: > 0 } group_num)
                    {
                        int number = int.Parse(group_num);
                        string value = match.Groups[number].Value;

                        replacement = replacement[..index] + value + replacement[(index + group_num.Length + 1)..];
                        index += value.Length;
                    }
                    else if (new string(replacement.Skip(index + 1).TakeWhile(char.IsLetter).ToArray()) is { Length: > 0 } group_name && match.Groups.ContainsKey(group_name))
                    {
                        string value = match.Groups[group_name].Value;

                        replacement = replacement[..index] + value + replacement[(index + group_name.Length + 1)..];
                        index += value.Length;
                    }
                    else
                        ++index;
                }
                while (index >= 0 && index < replacement.Length);

                replacement = replacement.Replace(DOLLAR_ESCAPE, "$");

                string to;

                if (arguments.ReplaceMatchOnly)
                    to = path[..match.Index] + replacement + path[(match.Index + match.Length)..];
                else if (Path.IsPathRooted(replacement))
                    to = replacement;
                else
                    to = entry.FullName[..(entry.FullName.Replace('/', '\\').LastIndexOf('\\') + 1)] + replacement;

                to = to.Replace('/', '\\');

                string displ = $"{display} -> {to.GetPath(arguments)}";

                if (arguments.DetailedFileList && !arguments.Quiet)
                    displ = $"{entry.DetailedPrefix()} {displ}";

                bool overwrite = arguments.ForceOverwrite;

                if ((Directory.Exists(to) || File.Exists(to)) && !overwrite)
                {
                    $"The file/directory \"{to}\" does already exist. Do you want to overwrite it? [y/N]".Print(ConsoleColor.White);

                    if (Console.ReadKey(true).Key is not ConsoleKey.Y)
                    {
                        if (!arguments.Quiet)
                            $"{display} will NOT be renamed to {to.GetPath(arguments)}.".Print(ConsoleColor.Red);

                        ++skipped;

                        continue;
                    }
                }

                if (!arguments.MockReplacement)
                    if (entry is DirectoryInfo directory)
                        directory.MoveTo(to);
                    else if (entry is FileInfo file)
                        file.MoveTo(to, overwrite);

                displ.Print(ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                ex.Message.Print(ConsoleColor.Red);
            }

        if (!arguments.Quiet)
        {
            int count = entries.Length - skipped;

            $"{(count > 0 ? count.ToString() : "No")} replacements performed in '{dir.FullName}'.".Print(count > 0 ? ConsoleColor.White : ConsoleColor.Yellow);
        }

        return 0;
    }

    public static IEnumerable<FileSystemInfo> Enumerate(DirectoryInfo dir, bool recursive)
    {
        foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo sub && recursive)
                foreach (FileSystemInfo subentry in Enumerate(sub, true))
                    yield return subentry;

            yield return entry;
        }
    }

    public static string DetailedPrefix(this FileSystemInfo nfo) =>
        $"{nfo.CreationTime:yyyy-MM-dd,HH:mm:ss} {nfo.LastWriteTime:yyyy-MM-dd,HH:mm:ss} {(nfo as FileInfo)?.Length ?? 0,15:##,#} | ";

    public static int Print(this string msg, ConsoleColor color, int ret = 0)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = ConsoleColor.Gray;

        return ret;
    }

    public static string GetPath(this string path, CLI_Base arguments)
    {
        if (!arguments.ShortNames)
            return path;

        StringBuilder sb = new(1024);

        GetShortPathName(path, sb, sb.Capacity);

        return sb.ToString() is { } s && !string.IsNullOrEmpty(s) ? s : path;
    }

    public static string ToHumanReadable(this long size)
    {
        string sizes = "KMGTEY";
        double len = size;
        int order = 0;

        while (len >= 1024 && order < sizes.Length)
        {
            ++order;
            len /= 1024;
        }

        return (order > 0 ? len.ToString("0.00") : len.ToString()) + (order == 0 ? "" : sizes[order - 1]) + "B";
    }
}

enum FilterMode
{
    All,
    DirsOnly,
    FilesOnly,
}
