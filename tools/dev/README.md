# Developer scripts

These scripts drive the real window on a Windows machine: they open the app, click
the sidebar, take screenshots and probe the DevTools port of a running profile.
They are how the interface is inspected during development.

Nothing in this folder is needed to build or run Joe Browser. The build is
`build\build.ps1` and `build\setup.ps1`; the checks are `JoeBrowser.exe --selftest`
and `tools\check-extension-fetch.py`.

The scripts assume a Windows account called Administrator and a 1920x1080 desktop,
so they are not a general purpose tool. They are kept because they are the only way
to see what a page actually looks like.
