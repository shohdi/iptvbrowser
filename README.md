# IPTV Browser for Windows and Xbox

This repository now includes a native C# UWP app in `IptvXbox.App` that:

- loads IPTV content from `channels.json` or directly from the Xtream Codes style API
- searches channels, movies, and series quickly using a flattened in-memory index
- groups results alphabetically
- sorts A-Z or Z-A
- plays live TV, movies, and series episodes inside the app with `MediaPlayerElement`
- targets `Windows.Universal`, which is the practical route for Windows desktop plus Xbox support

## Project notes

- Open `IptvXbox.sln` in Visual Studio 2022.
- Set `IptvXbox.App` as the startup project.
- For desktop testing, run on `Local Machine`.
- For Xbox, switch the target device to `Remote Machine` and deploy to your Xbox in Developer Mode.
