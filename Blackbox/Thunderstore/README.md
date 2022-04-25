# Blackbox Mod
This mod allows you to "blackbox" sets of buildings together to improve your UPS  
**WARNING**: This is an alpha release of the mod. Use at your own risk or if you would like to help support development of this mod  

## How to use this mod
* First install the mod, and reboot the game.
* Go into blueprint copy mode, select the input station(s) of your blackbox setup and press Ctrl + N  
* Press Ctrl + M to open up the Blackbox Manager Panel to view the blackboxed
* Use "Highlight" to view what buildings got grouped together as a blackbox
* Settings
  * You can untick "Analyse in background thread" if you want to blackbox in game time and check your blackbox setup for inefficiencies. Note that analysing in background thread is anywhere between 10x - 1000x faster.
  * Use Cycle Length Override to specify the duration of the recipe - otherwise the automatic duration detection uses a very high duration and may delay the analysis
  * Ideally, you should not need to toggle any other setting below except for "Auto-blackbox" (I still recommend manual blackboxing at this stage, since the auto-blackbox algorithm goes through the stations in the order they were placed. The algorithm cannot discriminate between blackboxable and non-blackboxable setups without simulating them, so it will probably waste a lot of time on non-blackboxable setups)
  * The rest of the settings are currently for debugging purpose. You will only need to change them if you have been in contact with me on the DSP Discord

I will provide clearer explanations once the mod is out of alpha. For now, I just want to get this out there  
You are encouraged to contact me on the DSP Discord to provide feedback

## Known issues

* The background benchmarking (default option) sometimes converges too fast with a suboptimal recipe. In such a case, you can try unticking the "Analyse in background thread" option and try again to see if it gives a better recipe
* The benchmarking takes a much longer time to converge, if the setup oversaturates the belt. This is because the excess items need to back up through the setup, till it reaches the input stations, for the recipe to stabilize

## Roadmap

* Maintain internal buffer for blackbox simulation
* Complete blackbox insufficient power logic
* Make the saturation phase run faster
* Support proliferated output
* Provide visual feedback regarding which item / recipe is preventing the setup from being blackboxable
* Figure out static analysis logic (if possible)

## Changelog

### [v0.0.8](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.8/)
* Fix issues in Item Saturation phase
* Fix power consumption recording for piler, spraycoater and traffic monitor
* Add support for fractionators in blackbox

### [v0.0.7](https://dsp.thunderstore.io/package/Raptor/Blackbox/0.0.7/)
* Fix spraycoater consumption benchmarking
* Fix power benchmarking for piler, spraycoater and traffic monitor
* Fix UI exception on hovering over certain blackboxed entities, like belts (due to game patch 0.9.25.11989)
* Revamp benchmarking logic (yet again). It should handle non-ratio setups much better now, instead of ending with "Analysis Failed"
* Add setting to override the cycle length of the blackbox recipe

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