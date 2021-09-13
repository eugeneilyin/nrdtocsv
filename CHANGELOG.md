# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2021-09-13
### Added
- Skip already converted files
- Estimated time to complete (ETA)
- Complete and total for files and bytes stat

### Changed
- Limit parallel conversion to 4 threads<br>
  (depending on a hard drive load (checked with `resmon` command)<br>
  you can adjust it with the `PARALLEL_THREADS_COUNT` constant)

### Fixed
- Fix minor typos

## [1.1.0] - 2021-09-12
### Added
- Progress bar indicator
- Ability to cancel conversion

### Changed
- Limit parallel conversion to 8 threads
  (enough for most CPU and Hard Drives loads during conversion)

## 1.0.0 - 2021-09-09
### Added
- Ability to specify output `*.csv` root directory
- Filter of `*.nrd` file names to convert based on RedExp

[Unreleased]: https://github.com/eugeneilyin/nrdtocsv/compare/v1.1.0...HEAD
[1.2.0]: https://github.com/eugeneilyin/nrdtocsv/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/eugeneilyin/nrdtocsv/compare/v1.0.0...v1.1.0
