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



## How it works


