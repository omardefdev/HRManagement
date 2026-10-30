1. dotnet list HRManagement.slnx package --vulnerable --include-transitive → no project with vulnerable packages.
2. scripts/ci/steps.json "Build" runs with -warnaserror and passed in the clean-HEAD Linux run;
   Directory.Build.props makes NU1903/NU1904 errors for every build.
3. Negative: a probe project under artifacts/ referencing SSH.NET 2025.1.0 fails restore with NU1903.