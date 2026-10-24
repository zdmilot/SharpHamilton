# Offline source contracts

This console harness links four unchanged production source files. It tests sequence boundaries and synthetic result decoding without loading the full STARLine assembly. The empty `Container` fixture provides identity only; the sequence does not use any container members. No Hamilton runtime, COM activation, UI, configuration access, or device connection is part of this project.

The inclusive sequence contract follows `Current` accepting `End`, the `PositionsLeft = End - Current + 1` calculation, and `Increment` exhausting only after `End`. Independently expected identity indices exercise all three `Get` overloads, including a single item, the final item, and a bounded subrange. `-1` in observations denotes a null output slot. Decoder literals are synthetic, not historical firmware evidence.

Use the installed audit-local .NET 8 SDK. Restore uses this project's empty NuGet source list and an isolated package directory. Wrap each command in the toolkit watchdog (timeout 180 seconds, idle 45 seconds):

```powershell
$env:PYTHONPATH = 'C:\Program Files (x86)\Hamilton\Tools and Utilities\Hamilton-Agent-Toolkit'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$auditSdk = 'C:\Users\User\AppData\Local\hamtk\audit\offline-20261023\dependencies\dotnet-8.0\dotnet.exe'
py -3 -B -m hamtk watchdog run --timeout 180 --idle 45 -- $auditSdk restore tests/OfflineContracts/OfflineContracts.csproj --configfile tests/OfflineContracts/NuGet.Config --packages C:\Users\User\AppData\Local\hamtk\audit\offline-20261023\dependencies\nuget --disable-parallel
py -3 -B -m hamtk watchdog run --timeout 180 --idle 45 -- $auditSdk build tests/OfflineContracts/OfflineContracts.csproj --no-restore --configuration Release
py -3 -B -m hamtk watchdog run --timeout 180 --idle 45 -- $auditSdk tests/OfflineContracts/bin/Release/net8.0/OfflineContracts.dll --source-root 'C:\Program Files (x86)\Hamilton\Tools and Utilities\SharpHamilton' --output '<absolute evidence JSON path>' --revision '<verified Git SHA>'
```

The program saves every expected and actual outcome, timing, CPU sample, process peak working set, input hash, and source hashes. An assertion failure returns exit code 1 after saving all results. Missing metrics are null, not zero. The final provenance case verifies that the linked and harness source hashes did not change during execution. Read the source hashes together with the baseline Git revision when the harness is not committed.

Physical qualification remains `NOT_PHYSICALLY_VALIDATED`. A successful run does not qualify the complete application or establish controller/firmware correctness.

The original revision `af6b19292aecf1b68a8268cfe31089158ba6d08a` produced 16 passing and 9 failing cases: each overload omitted the sole item, final item, and end item of a subrange. Changing only `< End` to `<= End` fixed those omissions but exposed a further exhausted-position failure: `Current = -1` plus output offset 2 accessed the first element. The final repair changes only those three predicates to require a valid `Current >= 1` and include `Current + i <= End`. All overloads now have exhausted and invalid-current regression cases. The original and intermediate failing evidence are retained outside the repository in the audit evidence folder; new runs must use distinct output paths to preserve them.
