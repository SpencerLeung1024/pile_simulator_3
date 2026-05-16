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
- - Chemistry: State functions, equations of state, simulator for what happens to Species inside a Volume, and devices with specific inputs and outputs
- Scripts: Anything bound to a Godot node, has _Ready and _Process, etc.
- Scenes: .tscn files
- docs: .md files
- - archive: old notes go here

### Scenes

Only World exists right now

- MainMenu: Lets you go to World, SolarSystem, Shop, or BoxSim
- World: An Asteroid that you can freecam around
- SolarSystem: Planets and asteroids are shown in orbits
- Shop: Buy and sell Species and Items. Location dependence is waiting on the fact that is no "you" and no "your location"
- BoxSim: A Volume and a test to see if the chemical simulation is working

## Guidelines
- Node references [Export] should be set in the Godot editor by me. Do not initialize in code or find in _Ready(). This reduces boilerplate and avoids bugs from different values
- Fail early, fail loudly. Errors are important and should not be concealed. Any fallbacks or handling should only be implemented by me after being aware
- The scene tree reflects the underlying game data. It is not the truth

## Current

- Implement code for thermodynamic simulations
- Specific things that need to be addressed:
- - How do I store the mass / energy of nuclides? Mass in Da? Binding energy in eV? Binding energy per nucleon? Are nuclear reactions exactly equal to changes in mass?
- - - Nuclides are not the top priority right now, but they will be important for nuclear reactions in the far future
- - How do I calculate the saturation pressure of a cubic EOS? https://en.wikipedia.org/wiki/Maxwell_construction is extremely difficult for me to follow
- - Implement the NASA7, NASA9, and Shomate heat capacity functions
- - Figure out how IdealGasEquation.GetU should work
- - - Why is internal energy U a function of entropy S and volume v?
- - Implement the incompressible phase, van der Waals, Redlich-Kwong, Soave-Redlich-Kwong, and Peng-Robinson EOS
- - Figure out how to architect Resource and Volume. What are fields and what are methods? What goes in what class? If I apply heat, how does the control flow?

- The dissociation / recombination loop will be handled later, after phase change works

- Put on hold because it's beyond my thinking ability:
- - Making the octree LOD faster
- - Removing voxels
- - Adding smaller rigid rocks from mining

## Future

- Research what materials, minerals, and elements exist in asteroids
- Research how mineral and chemical processing works in real life
- Figure out how to translate that into game mechanics
- Implement a solar system and orbit transfers
- Make the shop depend on your position
- Give the player and built structures inventories
- Buildable structures
- Archive the BoxSim (Volumes will be used in the World)
