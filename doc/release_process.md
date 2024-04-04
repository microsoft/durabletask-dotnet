# Release Process

1. Rev versions as appropriate following semver.
    - Repo-wide versions can be found in `eng/targets/Release.props`
    - Individual packages can version independently by adding the `<VersionPrefix>` and `<VersionSuffix>` properties directly in their `.csproj`.
    - We follow an approach of just releasing everything, even if the package has no changes. _Unless_ we intentionally do not want to release a package.
2. Ensure appropriate `RELEASENOTES.md` are updated in the repository.
    - These are per-package and contain only that packages changes.
    - The contents will be included in the `.nupkg` and show up in nuget.org's release notes tab.
    - Clear these files after a release.
3. Ensure `CHANGELOG.md` in repo root is updated.
4. Tag the release.
    - `git tag v<version>`, `git push <remote> -u <tag>`
5. Draft github release for new tag.
6. Kick off [ADO release build](https://dev.azure.com/durabletaskframework/Durable%20Task%20Framework%20CI/_build?definitionId=29) (use the tag as the build target, enter `refs/tags/<tag>`)
7. Validate signing, package contents (if build changes were made.)
8. Create ADO release.
    - From successful ADO release build, click 3 dots in top right -> 'Release'.
9. Release to ADO feed.
10. Release to nuget once validated (and dependencies are also released to nuget.)
11. Publish github draft release.
12. Delete contents of all `RELEASENOTES.md` files.
