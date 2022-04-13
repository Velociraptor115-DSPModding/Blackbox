# Blackbox Mod
This mod allows you to "blackbox" sets of buildings together to improve your UPS  
**WARNING**: This is an alpha release of the mod. Use at your own risk or if you would like to help support development of this mod  

## How to use this mod
* First install the mod, and reboot the game.
* Go into blueprint copy mode, select the input station(s) of your blackbox setup and press Ctrl + N  
* Press Ctrl + M to open up the Blackbox Manager Panel to view the blackboxed
* Use "Highlight" to view what buildings got grouped together as a blackbox
* Settings
  * Ideally, you should not need to toggle any setting except for "Auto-blackbox" (I still recommend manual blackboxing at this stage, since the auto-blackbox algorithm goes through the stations in the order they were placed. The algorithm cannot discriminate between blackboxable and non-blackboxable setups without simulating them, so it will probably waste a lot of time on non-blackboxable setups)
  * You can untick "Analyse in background thread" if you want to blackbox in game time and check your blackbox setup for inefficiencies
  * The rest of the settings are currently for debugging purpose. You will only need to change them if you have been in contact with me on the DSP Discord

I will provide clearer explanations once the mod is out of alpha. For now, I just want to get this out there  
You are encouraged to contact me on the DSP Discord to provide feedback

## Known issues

* The background benchmarking (default option) sometimes converges too fast with a suboptimal recipe. In such a case, you can try unticking the "Analyse in background thread" option and try again to see if it gives a better recipe
* The benchmarking takes a much longer time to converge, if the setup oversaturates the belt. This is because the excess items need to back up through the setup, till it reaches the input stations, for the recipe to stabilize

## Roadmap

* Support proliferated output
* Properly capture proliferator use in the consumption metrics
* Provide visual feedback regarding which item / recipe is preventing the setup from being blackboxable
* Figure out static analysis logic (if possible)

## Changelog

### [v0.0.6](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.6/)
* Fix bug introduced by consolidating power consumer logging in previous update
* Add keybind for Blackbox Manager Window

### [v0.0.5](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.5/)
* Fix support for spraycoaters, pilers and traffic monitors
* Consolidate power consumer logging to reduce memory required for benchmarking
* Add recipe display in the inspect window
* Add inspect button to the entries in the overview panel

### [v0.0.4](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.4/)
* Update code to support the Jan 20 update (game version 0.9.24.11182), i.e. spraycoaters and pilers

### [v0.0.3](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.3/)
* First working version, I think

## Contact / Feedback / Bug Reports
You can either find me on the DSP Discord's #modding channel  
Or you can create an issue on [GitHub](https://github.com/Velociraptor115/DSPMods)  
\- Raptor#4825