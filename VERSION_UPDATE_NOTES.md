# Version Update from 1.0.5 to 1.1.0

## Overview
Release v1.0.5 was incorrectly versioned. The changes in that release (High Scale Support with 10,421 lines of new code) represent a minor version bump according to semantic versioning, not a patch version.

## Changes Made in This PR
1. Updated `GitVersion.yml` to set `next-version: 1.1.0` (changed from `1.0.0`)
2. Created local tag `v1.1.0` pointing to commit `f1d4e6715f626430763387391024f179361278ea`
3. Deleted local tag `v1.0.5`

## Manual Steps Required After PR Merge

**IMPORTANT**: The following steps must be performed by someone with appropriate repository permissions:

### 1. Delete the old v1.0.5 tag from GitHub
```bash
git push origin :refs/tags/v1.0.5
```

### 2. Push the new v1.1.0 tag to GitHub
```bash
git push origin v1.1.0
```

### 3. Update the GitHub Release
- Delete or edit the existing "Release v1.0.5" on GitHub
- Create a new "Release v1.1.0" with the same content but updated version number
- Update the release notes to reflect the correct version

### 4. Update NuGet Package (if already published)
If the package was already published to NuGet.org as version 1.0.5:
- The package cannot be deleted from NuGet.org
- Publish a new version 1.1.0 with the same code
- Consider marking 1.0.5 as deprecated or unlisting it

## Semantic Versioning Justification
The v1.0.5 release included:
- High Scale Support feature
- 58 files changed
- 10,421+ lines of code added
- New test files, schemas, and comprehensive feature additions

According to [Semantic Versioning](https://semver.org/):
- MAJOR version for incompatible API changes
- MINOR version for new functionality in a backward-compatible manner
- PATCH version for backward-compatible bug fixes

The changes clearly represent new functionality (MINOR version), not a bug fix (PATCH version).
