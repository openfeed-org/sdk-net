## Release

We use GitHub Actions for the release process. It is a manual action that can be triggered in the repository.

### Steps:

1. Trigger the GitHub Action to build, sign, and package the `.nupkg` file.
2. Download the `NugetReleaseArtifacts` files from the workflow artifacts.
3. Log in to [NuGet.org](https://www.nuget.org/):
   - Go to **Account > Manage Organizations > Barchart > Edit Icon > Certificates** and upload the `.cer` file (if not already registered).
   - Go to **Account > Manage Packages** and upload the `.nupkg` file.
4. Verify that the package is published successfully.
