# WEB/02 verification

Local verification on 2026-11-03 used .NET SDK 10.0.100, MongoDB 8.0.15 and the repository's pinned
oasdiff 1.35.0 source build with Go 1.27.3. No dependency or lockfile changes were required.

| Check | Outcome |
| --- | --- |
| Locked NuGet restore with existing high/critical audit gate | Passed |
| Release build of all projects | Passed, zero warnings/errors |
| Entire .NET suite | 364 passed, zero failures/skips: API 81, Application 20, Domain 100, Infrastructure 51, MongoDB integration 112 |
| New real-MongoDB browsing tests | 8 passed; included in the integration total |
| Source/architecture/shell checks and deployment/image Python checks | Passed, including 11 Python tests |
| Folder-based whitespace formatting of changed C# files | Passed |
| Full solution `dotnet format` | Hosted CI check required; local MSBuild workspace loading is blocked by denied Unix named-pipe sockets |
| Docker image smoke and vulnerability scan | Hosted CI check required; Docker is unavailable locally |

The existing integration fixture's `PACT_TEST_MONGODB` option pointed to an actual local MongoDB
process over loopback TCP. This environment blocks Unix sockets, so only the local test server used
`--nounixsocket`; production configuration and test assertions were not weakened. Source comparison
tests used the real checksum-verified pinned oasdiff build. These results do not establish Azure
deployment or real consumer/provider source-CI pilot completion.

The new cases cover empty reads with no business writes, prefix/grouped application discovery,
filter/resource-scoped cursors, timestamp/ID ties, a newer insert during traversal, directed and
unregistered graph parties, metadata/original-byte links, same-issue filtering, all four report
statuses, exact version/CI report selection, declared gaps, global versus selected counts,
profile normalization, same-row decision filters, and corrupt-snapshot rejection. HTTP checks
exercise pre-storage validation, sanitized outages and generated typed OpenAPI.
