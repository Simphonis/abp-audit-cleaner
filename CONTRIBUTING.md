# Contribution guidelines
Thank you for considering contributing to the project. Please note that this is a tool designed to be used internally in our company, and we cannot give any guarantee that PRs and issues will be taken into account!

# Versionning
The project's versionning follows ABP's version numbers: for example, version 10.7.0 of the `Simphonis.Abp.Package` package must reference version 10.7.0 of the official ABP packages, which makes it easier to track which version of ABP is used in a project.

To publish a new version, create and push a new tag containing the version identifier, prefixed by a 'v'. For example: `v10.7.0`. Please note that a repository admin will have to validate the deployment for it to be fully pushed into new GitHub and Nuget releases.

You may create prerelease versions by adding a version suffix with a dash. For example, a good prerelease tag would be: `v10.7.0-rc.1`.

# Code style
All source code is expected to follow the style guidelines enumerated in the `.editorconfig` file.

Documentation, commit messages, comments, and identifiers should be written in english.
