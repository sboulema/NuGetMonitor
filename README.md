# NuGet Monitor
A Visual Studio extension that checks and notifies about available updates
for the installed NuGet packages for the open solution.

[![Build Status](https://github.com/sboulema/NuGetMonitor/actions/workflows/workflow.yml/badge.svg)](https://github.com/sboulema/NuGetMonitor/actions/workflows/workflow.yml)
[![Sponsor](https://img.shields.io/badge/-Sponsor-fafbfc?logo=GitHub%20Sponsors)](https://github.com/sponsors/sboulema)

## Features
- Check for updates, deprecations and vulnerabilities when a solution is opened
- Show InfoBar with update, deprecation and vulnerabilities count when any are found
- Works with .NET Framework projects and with .NET projects

## Support
- Visual Studio 2022

## Installing
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=SamirBoulema.NuGetMonitor) [![Visual Studio Marketplace](https://img.shields.io/vscode-marketplace/v/SamirBoulema.NuGetMonitor.svg?style=flat)](https://marketplace.visualstudio.com/items?itemName=SamirBoulema.NuGetMonitor)

[GitHub Releases](https://github.com/sboulema/NuGetMonitor/releases)

[Open VSIX Gallery](https://www.vsixgallery.com/extension/NuGetMonitor.2a6fbffe-f3fd-4bf8-98cc-5ae2c833a1c7)

## Screenshots
[![Screenshot](https://raw.githubusercontent.com/sboulema/NuGetMonitor/main/art/Screenshot2.png)](https://raw.githubusercontent.com/sboulema/NuGetMonitor/main/art/Screenshot2.png)

[![Screenshot](https://raw.githubusercontent.com/sboulema/NuGetMonitor/main/art/Screenshot.png)](https://raw.githubusercontent.com/sboulema/NuGetMonitor/main/art/Screenshot.png)

## Thanks

### AnushaG2201
A big thanks goes to [AnushaG2201](https://github.com/AnushaG2201)!

I was playing with the idea for this extension for quite a while but never figured out how I would create this extension. 

That is until I saw the [Nuget-updates-notifier](https://marketplace.visualstudio.com/items?itemName=Anusha.NugetPackageUpdateNotifier) ([GitHub](https://github.com/AnushaG2201/Nuget-updates-notifier)) which gave me the remaining puzzle pieces, so that I could create my own version.

### tom-englert
A big thanks goes to [tom-englert](https://github.com/tom-englert)!

A massive improvement [PR](https://github.com/sboulema/NuGetMonitor/pull/4) really improved the quality of this extension. 

## Links
[NuGet Client SDK / NuGet.Protocol](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk)

[Visual Studio Extensibility Cookbook - Notifications](https://www.vsixcookbook.com/recipes/notifications.html)

[Invoke the Manage NuGet Packages dialog programmatically](https://devblogs.microsoft.com/nuget/invoke-manage-nuget-packages-dialog-programmatically/)

[UpdatR packages](https://github.com/OskarKlintrot/UpdatR)