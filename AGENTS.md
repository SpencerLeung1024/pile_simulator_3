# Pile Simulator 3

A game about asteroid mining and processing
- Control a robot and interact with onboard and built systems
- Break down an asteroid's voxels for resources
- Use chemistry, thermodynamics, and various devices to process them
- Transfer across the solar system using an engine
- Sell stuff

Except basically none of that exists yet

Inspirations from Minecraft, various mining games on Roblox, Space Engineers, Stationeers, etc.

## Project Info

Godot 4.7.beta2, Forward+, Jolt Physics, C# 14, net10.0

### Directory
- Data: Constants, voxel cell materials, chemical species, elements, nuclides, countable items
- DSA: Anything not bound to a node
- - Voxel: Asteroid generator and octree
- - Gravity: "Physics" gravity (forces applied on nearby rigid bodies) and Keplerian orbits
- - Chemistry: State functions, equations of state, simulator for what happens inside a Volume
- - Devices: Things that interact with Networks and Volumes, with various inputs and outputs
- Scripts: Anything bound to a Godot node, has _Ready and _Process, etc.
- Scenes: .tscn files
- docs: .md files
- - archive: old notes go here

### Scenes

- MainMenu: Lets you go to World or BoxSim
- World: An Asteroid that you can freecam around
- BoxSim: A Volume and a test to see if the chemical simulation is working

## Guidelines
- Node references [Export] should be set in the Godot editor by me. Do not initialize in code or find in _Ready(). This reduces boilerplate and avoids bugs from different values
- Fail early, fail loudly. Errors are important and should not be concealed. Any fallbacks or handling should only be implemented by me after being aware
- The scene tree reflects underlying game data. It is not the truth
- You have no way of testing at runtime or interacting with the Godot editor. The only way to check if code works is to ask me to test
- OpenCode explore subagents should inform their parent agent of filepaths and line ranges to read. Duplicating file contents verbatim in the output is error-prone. Only write specific content if annotations are needed

## Current

- Implement code for thermodynamic simulations

- Answered by DeepSeek V4 Pro:
- - P_sat in a cubic EOS should be found iteratively
- - Use fugacity to move moles of a species between phases
- - Use the Element Potential Method to find the equilibrium of the reaction
- - Resource contains SpeciesPhase and n only. It does not store thermodynamic variables. Volume handles conservation of various things
- - - EDIT: Resource has been renamed to SpeciesPhaseResource
- - Use NASA9 for all heat capacity functions for now and leave Shomate for another time

- Answered by GPT-5.5 and Opus 4.7:
- - The dual problem is a system of (num elements + num phases) non-linear equations. It combines element usage constraints and mole fraction normalization constraints. How to set it up is in `docs/chemistry/dual_problem`

- Solver architecture review by GPT-5.5, Opus 4.7, DeepSeek V4 Pro, and Kimi K2.6 is in `docs/chemistry/solver`

- MainMenu.cs calls Initialize of Elements, AllSpecies, FormulaTable, and Nuclides
```
118 Elements: 1.42 ms
6 Species and 9 Species Phases: 45.06 ms
Formulas: 8.37 ms
3558 Nuclides: 14.39 ms
```

- BoxSim.cs requirements:
- - MultiMeshSpeciesPhase has one mesh for each species
- - Phases are vertically stratified: gases, liquids, solids from top to bottom
- - Within each phase, species phases go from left to right in arbitrary order
- - Each mesh corresponding to a species phase is proportional to its volume
- - Gases are the original color, liquids are 25% darker, solids are 50% darker
- - Depth is always 100%
- - BoxSim calls Volume.GetInfo, which returns whatever information BoxSim needs
- - The SpeciesPhaseDropdown is populated
- - You should be able to add an amount of species at a temperature
- - ThermodynamicsLabel and ResourcesLabel are filled in according to the placeholder text in BoxSim.tscn
- - Play, Pause, Step 1 Frame, and Clear Contents
- - SparkCheck allows chemicals to react regardless of temperature

- Right now Volume.Solve is propagating NaNs for some reason
```
UpdateResourcesLabel:
CH4
Gas 200 3.2085999999999997 4.8747694330232445
O2
Gas 100 3.1997999999999998 2.4373847165116223
UpdateResourcesLabel:
C
Gas NaN NaN NaN
Solid NaN NaN NaN
CH4
Gas NaN NaN NaN
CO2
Gas NaN NaN NaN
H2
Gas NaN NaN NaN
H2O
Gas NaN NaN NaN
Liquid NaN NaN NaN
Solid NaN NaN NaN
O2
Gas NaN NaN NaN
```

- DeepSeek V4 Pro was able to stop NaNs from appearing using a lot of conditioning, but the box is still making too much H2O (474 mols instead of 200)

- Implied assumptions:
- - There is one state for the entire box. All SpeciesPhases obey the same temperature and pressure from Volume
- - The equilibrium of the reaction assumes ideal gases, ideal liquid solutions, and ideal solid solutions
- - Pressure, volume, and fugacity of gases and liquids is obtained from a cubic EOS, and that equilibrium will differ from the equilibrium of the reaction

- Put on hold:
- - Find a source of Shomate equation coefficients (like the NIST Chemistry WebBook) that is machine-processable and not a big PDF of tabulated 100 K increments
- - Make the octree LOD faster
- - Removable voxels
- - Add smaller rigid rocks from mining

## Future

- Research what materials, minerals, and elements exist in asteroids
- Research how mineral and chemical processing works in real life
- Figure out how to translate that into game mechanics
- Implement a solar system and orbit transfers
- Make a shop where you can buy and sell Resources and Items depending on your World location
- Give the player and built structures inventories
- Buildable structures
- Archive the BoxSim (Volumes will be used in the World)
