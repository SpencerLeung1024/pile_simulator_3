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

- Solver debugging by DeepSeek V4 Pro and GPT-5.5 is in `docs/chemistry/solver_debug`
- - DeepSeek V4 Pro: ended up making 474 mols of H2O ice
- - GPT 5.5: got rid of liquids and solids, solution makes 100 mols of CO2 and 200 mols of H2
- - My current implementation: got rid of liquids and solids, solution ends up mostly gaseous H2O with bits of CO2 and gaseous C

- Questions for members of the Council:
1. Check out `docs/chemistry/solver_debug_2` `cearun.txt` and `boxsim.txt`. I assume CEA's output is correct since, you know, NASA actually depends on the numbers. Can you explain what's going on and why CEA found that solution? Apart from lacking a CO species, what other simplifications in BoxSim lead to the equilibrium being wrong?
2. Why is my solver trying to make gaseous carbon at 1000 K?
3. I still feel that minoring out liquids and solids to stabilize J is a lazy fix. What, exactly, makes multi-phase equilibrium so much harder than gas-only? Does it make the problem non-convex? I know that CEA, STANJAN, etc. use an outer loop to turn condensed species on and off. Is this the most elegant, closest to real lifeway or is it for practical reasons?

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
