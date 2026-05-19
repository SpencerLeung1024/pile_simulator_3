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
- - Chemistry: State functions, equations of state, simulator for what happens inside a Volume, and devices with specific inputs and outputs
- Scripts: Anything bound to a Godot node, has _Ready and _Process, etc.
- Scenes: .tscn files
- docs: .md files
- - archive: old notes go here

### Scenes

Only World exists right now

- MainMenu: Lets you go to World, SolarSystem, Shop, or BoxSim
- World: An Asteroid that you can freecam around
- SolarSystem: Planets and asteroids are shown in orbits
- Shop: Buy and sell Resources and Items. Location dependence is waiting on the fact that there is no "you" and no "your location"
- BoxSim: A Volume and a test to see if the chemical simulation is working

## Guidelines
- Node references [Export] should be set in the Godot editor by me. Do not initialize in code or find in _Ready(). This reduces boilerplate and avoids bugs from different values
- Fail early, fail loudly. Errors are important and should not be concealed. Any fallbacks or handling should only be implemented by me after being aware
- The scene tree reflects underlying game data. It is not the truth
- You have no way of testing at runtime or interacting with the Godot editor. The only way to check if code works is to ask me to test

## Current

- Implement code for thermodynamic simulations

- Answered by DeepSeek V4 Pro:
- - Store nuclide binding energy in eV per nucleon. That is the most convenient form found online. Calculate nuclide mass as Z * proton mass + N * neutron mass - (Z + N) * binding energy per nucleon. All energy changes are product mass - reactant mass
- - Internal energy U *can* theoretically be calculated as U(S, v), but U(T, v) is more practical
- - P_sat in a cubic EOS should be found iteratively
- - Use fugacity to move moles of a species between phases
- - Use the Element Potential Method to find the equilibrium of the reaction
- - Resource contains SpeciesPhase and n only. It does not store thermodynamic variables. Volume handles conservation of various things
- - - EDIT: Resource has been renamed to SpeciesPhaseResource

- Answered by GPT-5.5 and Opus 4.7:
- - The dual problem is a system of (num elements + num phases) non-linear equations. It combines element usage constraints and mole fraction normalization constraints. How to set it up is in `docs/chemistry/dual_problem`

### Entry Point (If you are the top-level OpenCode agent read this)
- Apparently NASA Chemical Equilibrium with Applications got a rewrite in December 2025 with a standalone executable and a Python package
- - The source is publicly viewable: https://github.com/nasa/cea
- - There is even an included NASA9 polynomial dataset: https://github.com/nasa/cea/blob/main/data/thermo.inp
- - I have copied it to `DSA/Data/thermo.inp`
- - It is a 15802 line text file. Here are the entries for water:

``` line 5755
H2O               Hf:Cox,1989. Woolley,1987. TRC(10/88) tuv25.
 2 g 8/89 H   2.00O   1.00    0.00    0.00    0.00 0   18.0152800    -241826.000
    200.000   1000.0007 -2.0 -1.0  0.0  1.0  2.0  3.0  4.0  0.0         9904.092
-3.947960830D+04 5.755731020D+02 9.317826530D-01 7.222712860D-03-7.342557370D-06
 4.955043490D-09-1.336933246D-12                -3.303974310D+04 1.724205775D+01
   1000.000   6000.0007 -2.0 -1.0  0.0  1.0  2.0  3.0  4.0  0.0         9904.092
 1.034972096D+06-2.412698562D+03 4.646110780D+00 2.291998307D-03-6.836830480D-07
 9.426468930D-11-4.822380530D-15                -1.384286509D+04-7.978148510D+00
```

``` line 12481
H2O(cr)           Ice. Gordon,1982.
 1 g11/99 H   2.00O   1.00    0.00    0.00    0.00 1   18.0152800    -299108.000
    200.000    273.1507 -2.0 -1.0  0.0  1.0  2.0  3.0  4.0  0.0            0.000
-4.026777480D+05 2.747887946D+03 5.738336630D+01-8.267915240D-01 4.413087980D-03
-1.054251164D-05 9.694495970D-09                -5.530314990D+04-1.902572063D+02
H2O(L)            Liquid. Cox,1989. Haar,1984. Keenan,1984. Stimson,1969.
 2 g 8/01 H   2.00O   1.00    0.00    0.00    0.00 2   18.0152800    -285830.000
    273.150    373.1507 -2.0 -1.0  0.0  1.0  2.0  3.0  4.0  0.0        13278.000
 1.326371304D+09-2.448295388D+07 1.879428776D+05-7.678995050D+02 1.761556813D+00
-2.151167128D-03 1.092570813D-06                 1.101760476D+08-9.779700970D+05
    373.150    600.0007 -2.0 -1.0  0.0  1.0  2.0  3.0  4.0  0.0        13278.000
 1.263631001D+09-1.680380249D+07 9.278234790D+04-2.722373950D+02 4.479243760D-01
-3.919397430D-04 1.425743266D-07                 8.113176880D+07-5.134418080D+05
```

- The Shomate equations in the NIST Chemistry WebBook should be better for condensed phases. Unfortunately there seems to be no way to filter species by those with Shomate equations, or to get all of them as a dataset
- - Most Shomate equations are listed as coming from: Chase, M.W., Jr., NIST-JANAF Themochemical Tables, Fourth Edition, J. Phys. Chem. Ref. Data, Monograph 9, 1998, 1-1951.
- - https://webbook.nist.gov/cgi/cbook.cgi?Source=1998CHA1-1951 shows the first 400 species from that book, so those are probably guaranteed to have Shomate equations
- - There seems to be a website where you can browse a species page from that source: https://janaf.nist.gov/
- - But that gives me a *table* with temperature steps of 100 K, not Shomate coefficients

- Elements are already done. They were the easiest data to fill. There's only 118 of them
- - I just copied all the numbers from https://en.wikipedia.org/wiki/Standard_atomic_weight#List_of_atomic_weights
- - That Wikipedia table, in turn, came from IUPAC through https://www.degruyterbrill.com/document/doi/10.1515/pac-2019-0603/html

- Nuclides are harder though, because NuDat says it's just a website that queries several databases
- - It says its source of nuclide binding energies is: https://iopscience.iop.org/article/10.1088/1674-1137/abddaf
- - That's a 2.58 MB, 513 page PDF
- - It's at least rendered lines instead of scanned typewriter font with a professor's notes drawn on top like the STANJAN PDF
- - I would have preferred .json or something though

- Implied assumptions:
- - There is one state for the entire box. All SpeciesPhases obey the same temperature and pressure from Volume
- - The equilibrium of the reaction assumes ideal gases, ideal liquid solutions, and ideal solid solutions
- - Pressure, volume, and fugacity of gases and liquids is obtained from a cubic EOS, and that equilibrium will differ from the equilibrium of the reaction

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
