using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Huarui.STARLine;

internal static class Program
{
    private static readonly List<object> Records = new List<object>();
    private static int failed;
    private static string revision;
    private static string output;
    private static readonly string[] LinkedSources = {
        "src/STARLine/Deck/ContainerSequence.cs",
        "src/STARLine/StepReturn/StepResult.cs",
        "src/STARLine/StepReturn/StepResultErrorFlag.cs",
        "src/STARLine/Error/RecoveryAction.cs"
    };
    private static readonly string[] HarnessSources = {
        "tests/OfflineContracts/Program.cs", "tests/OfflineContracts/FixtureContainer.cs",
        "tests/OfflineContracts/OfflineContracts.csproj", "tests/OfflineContracts/NuGet.Config"
    };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    private static string Json(object value) { return JsonSerializer.Serialize(value); }
    private static string Hash(byte[] bytes) { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); }
    private static Dictionary<string, string> HashFiles(string root, IEnumerable<string> paths)
    {
        return paths.ToDictionary(p => p, p => Hash(File.ReadAllBytes(Path.Combine(root, p))));
    }
    private static void Check(string id, object input, object expected, Func<object> observe)
    {
        var timestamp = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        object actual;
        string error = null;
        try { actual = observe(); }
        catch (Exception ex) { actual = new { exception = ex.GetType().Name }; error = ex.ToString(); }
        timer.Stop();
        process.Refresh();
        var pass = Json(actual) == Json(expected);
        if (!pass) failed++;
        Records.Add(new {
            test_id = id, utility = "SharpHamilton", revision,
            test_input = new { kind = "synthetic", fixture = input, sha256 = Hash(Encoding.UTF8.GetBytes(Json(input))) },
            expected_outcome = expected, actual_outcome = actual, verdict = pass ? "PASS" : "FAIL",
            execution_ms = timer.Elapsed.TotalMilliseconds,
            cpu_ms = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            peak_memory_bytes = process.PeakWorkingSet64,
            peak_memory_scope = "process lifetime peak sampled after this case, not isolated case allocation",
            gpu_ms = (double?)null, gpu_memory_bytes = (long?)null,
            error_output = error, exit_code = (int?)null,
            exit_code_note = "in-process case; executable exit code recorded at report level",
            environment = new { runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), process_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
            evidence = Path.GetFullPath(output), timestamp = timestamp.ToString("O")
        });
        Console.WriteLine("{0} {1} {2:F3} ms expected={3} actual={4}", pass ? "PASS" : "FAIL", id, timer.Elapsed.TotalMilliseconds, Json(expected), Json(actual));
    }
    private static ContainerSequence Sequence(int count)
    {
        var seq = new ContainerSequence();
        for (var i = 0; i < count; i++) seq.Add(new Container());
        return seq;
    }
    private static int[] Indices(ContainerSequence seq, Container[] items)
    {
        return items.Select(item => item == null ? -1 : seq.IndexOf(item)).ToArray();
    }
    private static Container[] Get(ContainerSequence seq, int mode, int size)
    {
        if (mode == 0) return seq.Get(size);
        var destination = Enumerable.Repeat(new Container(), size).ToArray();
        if (mode == 1) seq.Get(destination, size); else seq.Get(destination);
        return destination;
    }
    private static object State(ContainerSequence seq)
    {
        return new { current = seq.Current, end = seq.End, positions_left = seq.PositionsLeft };
    }
    private static void TestSequences()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            var overload = mode;
            Check("sequence.single.get-" + mode, new { count = 1, current = 1, end = 1, overload }, new[] { 0 }, () => {
                var seq = Sequence(1); return Indices(seq, Get(seq, overload, 1));
            });
            Check("sequence.final.get-" + mode, new { count = 3, current = 3, end = 3, overload }, new[] { 2 }, () => {
                var seq = Sequence(3); seq.Current = 3; return Indices(seq, Get(seq, overload, 1));
            });
            Check("sequence.inclusive-subrange.get-" + mode, new { count = 4, current = 2, end = 3, size = 3, overload }, new[] { 1, 2, -1 }, () => {
                var seq = Sequence(4); seq.End = 3; seq.Current = 2; return Indices(seq, Get(seq, overload, 3));
            });
            Check("sequence.exhausted.get-" + mode, new { count = 3, increment = 3, size = 5, overload }, new[] { -1, -1, -1, -1, -1 }, () => {
                var seq = Sequence(3); seq.Increment(3); return Indices(seq, Get(seq, overload, 5));
            });
            Check("sequence.invalid-current.get-" + mode, new { count = 3, current = 0, size = 5, overload }, new[] { -1, -1, -1, -1, -1 }, () => {
                var seq = Sequence(3); seq.Current = 0; return Indices(seq, Get(seq, overload, 5));
            });
        }
        Check("sequence.single.inclusive-state", new { count = 1 }, new { current = 1, end = 1, positions_left = 1 }, () => State(Sequence(1)));
        Check("sequence.subrange.inclusive-state", new { count = 4, current = 2, end = 3 }, new { current = 2, end = 3, positions_left = 2 }, () => {
            var seq = Sequence(4); seq.End = 3; seq.Current = 2; return State(seq);
        });
        Check("sequence.increment.within-range", new { count = 3, increment = 1 }, new { current = 2, end = 3, positions_left = 2 }, () => {
            var seq = Sequence(3); seq.Increment(1); return State(seq);
        });
        Check("sequence.increment.exhaustion", new { count = 1, increments = new[] { 1, 1 } }, new { current = -1, end = 1, positions_left = 0 }, () => {
            var seq = Sequence(1); seq.Increment(1); seq.Increment(1); return State(seq);
        });
        Check("sequence.increment.to-final", new { count = 3, increment = 2 }, new { current = 3, end = 3, positions_left = 1 }, () => {
            var seq = Sequence(3); seq.Increment(2); return State(seq);
        });
        Check("sequence.empty", new { count = 0 }, new { current = -1, end = 0, positions_left = 0 }, () => State(Sequence(0)));
        Check("sequence.invalid-current", new { count = 3, current = 0 }, new { current = -1, end = 3, positions_left = 0 }, () => {
            var seq = Sequence(3); seq.Current = 0; return State(seq);
        });
        Check("sequence.invalid-end", new { count = 3, end = 4 }, new { current = -1, end = -1, positions_left = 0 }, () => {
            var seq = Sequence(3); seq.End = 4; return State(seq);
        });
        Check("sequence.destination-tail-cleared", new { count = 3, destination_size = 3, size = 1 }, new[] { 0, -1, -1 }, () => {
            var seq = Sequence(3); var destination = Enumerable.Repeat(seq[1], 3).ToArray(); seq.Get(destination, 1); return Indices(seq, destination);
        });
        Check("sequence.exhausted-get", new { count = 1, increment = 1, size = 3 }, new[] { -1, -1, -1 }, () => {
            var seq = Sequence(1); seq.Increment(1); return Indices(seq, seq.Get(3));
        });
    }
    private static void TestSyntheticResults()
    {
        Check("step-result.empty-block-list", "0", new { error_flag = 0, block_count = 0 }, () => {
            var result = StepResult.Parse("0"); return new { error_flag = (int)result.ErrorFlag, block_count = result.Blocks.Count };
        });
        const string fixture = "1[1,2,3,4,DATA,Lab,A1[2,0,0,0,,Other,B2";
        Check("step-result.synthetic-two-blocks", fixture, new {
            error_flag = 1,
            blocks = new[] {
                new { position = 1, main_error = 2, slave_error = 3, recovery = 4, data = "DATA", labware = "Lab", well = "A1" },
                new { position = 2, main_error = 0, slave_error = 0, recovery = 0, data = "", labware = "Other", well = "B2" }
            }
        }, () => {
            var result = StepResult.Parse(fixture);
            return new { error_flag = (int)result.ErrorFlag, blocks = result.Blocks.Select(b => new { position = b.Position, main_error = b.MainError, slave_error = b.SlaveError, recovery = (int)b.Recovery, data = b.StepData, labware = b.LabwareName, well = b.LabwarePos }).ToArray() };
        });
        Check("step-result.invalid-number", "x", new { exception = "FormatException" }, () => StepResult.Parse("x"));
        Check("step-result.truncated-fields", "0[1,2", new { exception = "IndexOutOfRangeException" }, () => StepResult.Parse("0[1,2"));
        Check("step-result.empty-input", "", new { exception = "FormatException" }, () => StepResult.Parse(""));
    }
    public static int Main(string[] args)
    {
        if (args.Length != 6 || args[0] != "--source-root" || args[2] != "--output" || args[4] != "--revision")
        {
            Console.Error.WriteLine("Usage: OfflineContracts --source-root PATH --output PATH --revision SHA"); return 2;
        }
        var root = Path.GetFullPath(args[1]); output = Path.GetFullPath(args[3]); revision = args[5];
        var before = HashFiles(root, LinkedSources.Concat(HarnessSources));
        var started = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        TestSequences(); TestSyntheticResults();
        var after = HashFiles(root, LinkedSources.Concat(HarnessSources));
        Check("provenance.sources-unchanged", before, before, () => after);
        total.Stop();
        var exitCode = failed == 0 ? 0 : 1;
        var report = new {
            schema_version = 1, utility = "SharpHamilton", revision, source_root = root,
            source_hashes = before, source_hashes_after = after,
            fixture_kind = "synthetic", started = started.ToString("O"), completed = DateTimeOffset.UtcNow.ToString("O"),
            total_tests = Records.Count, passing = Records.Count - failed, failing = failed, execution_ms = total.Elapsed.TotalMilliseconds,
            exit_code = exitCode, records = Records,
            limitations = new[] {
                "Tests compile the four linked production sources, not the complete .NET Framework/COM application.",
                "Container is an identity-only test fixture; production Container behavior is not tested.",
                "StepResult literals are synthetic grammar cases, not historical controller or firmware evidence.",
                "Invalid-input exception cases describe rejection and do not establish a public exception-type guarantee.",
                "Peak memory is the executable lifetime working-set peak; per-case allocation peaks are unavailable.",
                "Revision identifies the repository baseline; source hashes identify the exact tested files, including uncommitted harness files.",
                "No device, COM, WinForms, WPF, configuration, or physical behavior was invoked or qualified."
            },
            physical_qualification = "NOT_PHYSICALLY_VALIDATED"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
        Console.WriteLine("TOTAL {0}: {1} PASS, {2} FAIL; {3:F3} ms; evidence={4}", Records.Count, Records.Count - failed, failed, total.Elapsed.TotalMilliseconds, output);
        return exitCode;
    }
}
