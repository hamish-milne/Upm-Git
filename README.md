# Upm-Git
A Unity Package Manager bridge for Git repositories

Got some [custom packages](https://docs.unity3d.com/Manual/CustomPackages.html) on a Git repository somewhere that you want to use in your projects? Upm-Git is a straightforward way to have them show up in the Unity Package Manager window, with full support for versioning and dependencies.

## Quick Start - Using the UPM Settings window

* Download the latest release of `Upm-Settings.unitypackage` from the Releases page and install it into your project
* Open Editor -> Settings -> UPM and add a new Scoped Registry with the URL of your repository
* Reload the Package list

## Quick start - Manual

* Download the latest release of `Upm-Git` from the Releases page and save it to the special folder `shell:startup` (run it immediately to get started)
* [URL-encode](https://meyerweb.com/eric/tools/dencoder/) your Git remote, e.g. `git@github.com/hamish-milne/Upm-Git` becomes `git%40github.com%2Fhamish-milne%2FUpm-Git`
* Add a [scoped registry](https://docs.unity3d.com/Manual/upm-scoped.html) to your `Packages/manifest.json` file, with the URL `http://localhost:8760/<your encoded URL>`. The Name and Scopes can be set as desired.
* Reload the Package list

## Requirements

* The host machine needs a functional Git client. Unless otherwise specified, it must be executable as `git` (i.e. added to PATH)
* The remote repository must support `git-archive`

## Configuration



## Why Upm-Git?

Unity provides a few ways to import custom packages already:

* Using an NPM registry like Verdaccio. This gives you 'full' package management (dependencies, versioning, UI), but requires CI to publish packages to it, and for practical use requires running it as a globally available web service. Furthermore since Unity doesn't support authentication for scoped registries, you'll need to restrict access to a LAN/VLAN if you want to keep it private.
* Directly referencing the repository in the manifest. This is definitely the simplest solution, however it doesn't give you 'full' support, and requires that each package have its own repository.
* Using a git submodule and adding the packages as local files. This works, and is the way to go while you're developing the packages as you can easily push your changes upstream, but doesn't give you 'full' support and requires manually managing the submodule's status.

Upm-Git has some advantages:

* You get 'full' package management support: dependencies, versioning, and the use of the built-in package manager UI
* No extra publishing or copying is required; the service reads directly from git
* The app can be run locally, avoiding the need for a separate web service in private use scenarios
* Since it is just a web app, you can publish your packages by simply running it on an accessible server
* By default the service is configuration-less, and can operate on any repository specified through the URL. There's no setup required for the repository; as long as it has `package.json` files, it can be scanned.

## How it works


