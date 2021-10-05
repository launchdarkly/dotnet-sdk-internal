# Change log

All notable changes to `LaunchDarkly.InternalSdk` will be documented in this file. For full release notes for the projects that depend on this project, see their respective changelogs. This file describes changes only to the common code. This project adheres to [Semantic Versioning](http://semver.org).

## [2.1.1] - 2021-10-05
### Changed:
- Updated dependency versions to keep in sync with dependencies of `LaunchDarkly.ServerSdk`.

## [2.1.0] - 2021-09-21
### Added:
- Made `StateMonitor` and `TaskExecutor` public; they had mistakenly been marked internal.

## [2.0.0] - 2021-09-21
### Added:
- `StateMonitor`, `TaskExecutor`.

### Changed:
- Moved `AtomicBoolean` and `AsyncUtils` into new namespace `LaunchDarkly.Sdk.Internal.Concurrent`.

### Removed:
- `MultiNotifier` (obviated by `StateMonitor`).

## [1.1.2] - 2021-06-07
### Fixed:
- Updated minimum `LaunchDarkly.CommonSdk` version to latest patch, to exclude versions of `LaunchDarkly.JsonStream` with a known parsing bug.

## [1.1.1] - 2021-05-07
### Fixed:
- `HttpProperties.ConnectTimeout` now really sets the TCP connect timeout, if the platform is .NET Core or .NET 5.0. Other platforms don&#39;t support connect timeouts and will continue to ignore this value.

## [1.1.0] - 2021-04-27
### Added:
- `UriExtensions`

### Fixed:
- Improved coverage and reliability of HTTP tests using `LaunchDarkly.TestHelpers`.

## [1.0.0] - 2021-02-08
Initial release of this package, to be used in `LaunchDarkly.ServerSdk` 6.0 and `LaunchDarkly.XamarinSdk` 2.0.
