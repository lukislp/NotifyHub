# Contributing to NotifyHub

Thanks for taking the time. NotifyHub is a single-maintainer project, so the process is
deliberately small - but it is the same for every change, including the maintainer's own.

## How changes get in

1. Open an issue first for anything bigger than a typo or an obvious bug fix, so the direction can
   be agreed before you spend time on it. Use the templates under `.github/ISSUE_TEMPLATE/`.
2. Fork the repository (or branch, if you have write access) and make your change on a branch.
3. Open a pull request against `main`. The pull-request template asks for what changed and why.
4. `main` is protected: a PR merges only after the test stage of
   [`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml) is green and the branch is up to
   date with `main` (enable auto-merge and it lands on its own once that is the case). Nobody
   pushes to `main` directly, not even the maintainer.

## What a pull request needs

- **Conventional Commits.** The version and the changelog are generated from the commit messages
  (`feat:` = minor release, `fix:` = patch release, `build:`/`ci:`/`docs:`/`test:` = no release).
  Squash-merge keeps the PR title as the commit message, so give the PR a Conventional Commit
  title.
- **Green required checks.** `test`, `build` and `review / dependency-review` are required; a red
  one blocks the merge.
- **Tests without live provider accounts.** New channels and behaviour come with tests in
  `tests/NotifyHub.Tests`, written against a fake `HttpMessageHandler` rather than a live Web
  Push, APNs or FCM account. A PR that adds behaviour without a test is asked to add one.
- **Both target frameworks.** The libraries multi-target `net8.0;net10.0`, so a change must
  compile and behave on both. Do not reach for an API that only exists on the newer one without
  guarding it.
- **Warnings.** Warnings are errors repo-wide (`TreatWarningsAsErrors` in
  `Directory.Build.props`) - do not silence one without saying why in the PR. This repository has
  no separate format or audit job; the compiler is the gate.
- **Lock files.** Projects carry a `packages.lock.json` and CI restores with `--locked-mode`, so a
  csproj that disagrees with its lock file fails the restore instead of silently updating it. A
  plain `dotnet restore NotifyHub.slnx` refreshes them locally - commit the result.
- **Public API changes.** This ships as a library. A renamed or removed public member is a
  breaking change for consumers - call it out in the PR body so the release notes can carry it.

## Running things locally

You need **both** the .NET 8 and .NET 10 SDKs installed, because the libraries multi-target
`net8.0;net10.0`.

```bash
dotnet test
```

The same commands CI runs:

```bash
dotnet restore tests/NotifyHub.Tests/NotifyHub.Tests.csproj --locked-mode
dotnet test tests/NotifyHub.Tests/NotifyHub.Tests.csproj --configuration Release --no-restore
dotnet restore NotifyHub.slnx --locked-mode
dotnet build NotifyHub.slnx --configuration Release --no-restore
```

`samples/` holds runnable examples of the channel-agnostic API.

## Releases

Releases pack the nupkgs and attach them to the GitHub Release. They are deliberately **not**
pushed to nuget.org, so consumers fetch the package from the release assets.

## Security issues

Please do not open a public issue for a vulnerability - use the private reporting path described
in [SECURITY.md](SECURITY.md). The [Code of Conduct](CODE_OF_CONDUCT.md) applies to every
interaction in this repository.
