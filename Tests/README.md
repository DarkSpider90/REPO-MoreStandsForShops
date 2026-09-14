# Audit regression tests

The audit tests are a zero-dependency executable so they can run without Unity, Photon,
the game, or external NuGet packages:

```powershell
dotnet restore Tests/MoreStandsForShops.AuditTests/MoreStandsForShops.AuditTests.csproj --configfile Tests/NuGet.Config
dotnet run --project Tests/MoreStandsForShops.AuditTests/MoreStandsForShops.AuditTests.csproj -c Release --no-restore
```

They compile the pure stock-routing, preset, and reroll-transaction production sources
directly. Architectural regression checks also protect item rotations, Photon room-object
spawning, stand events, host-migration recovery hooks, incremental layout retries, and
one-shot table placement. They also guard reversible first-grab table stabilization,
stabilization recovery after a master switch, bounded C.A.R.T. auditing, and settled
upgrade-stand spring work. Unity physics, actual Photon room migration, late joining, and
real bandwidth still require the in-game checklist documented in
`MANUAL-TEST-CHECKLIST-RU.md`.
