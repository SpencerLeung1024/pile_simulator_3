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
- - P_sat in a cubic EOS should be found iteratively
- - Use fugacity to move moles of a species between phases
- - Use the Element Potential Method to find the equilibrium of the reaction
- - Resource contains SpeciesPhase and n only. It does not store thermodynamic variables. Volume handles conservation of various things
- - - EDIT: Resource has been renamed to SpeciesPhaseResource
- - Use NASA9 for all heat capacity functions for now and leave Shomate for another time

- Answered by GPT-5.5 and Opus 4.7:
- - The dual problem is a system of (num elements + num phases) non-linear equations. It combines element usage constraints and mole fraction normalization constraints. How to set it up is in `docs/chemistry/dual_problem`

### Species Loading
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

### Nuclide Loading
- The Atomic Mass Data Center (https://www-nds.iaea.org/amdc/) provides two relevant files which I have copied
- - `DSA/Data/mass_1.mas20.txt`, a 3594 line text file, for nuclide binding energy

``` line 35
1N-Z    N    Z   A  EL    O     MASS EXCESS           BINDING ENERGY/A        BETA-DECAY ENERGY               ATOMIC MASS
                                   (keV)                  (keV)                    (keV)                        (micro-u)
0  1    1    0    1  n         8071.31806     0.00044       0.0        0.0     B-    782.3470     0.0004    1 008664.91590     0.00047
  -1    0    1    1 H          7288.971064    0.000013      0.0        0.0     B-      *                    1 007825.031898    0.000014
0  0    1    1    2 H         13135.722895    0.000015   1112.2831     0.0002  B-      *                    2 014101.777844    0.000015
0  1    2    1    3 H         14949.81090     0.00008    2827.2654     0.0003  B-     18.59202    0.00006   3 016049.28132     0.00008
  -1    1    2    3 He        14931.21888     0.00006    2572.68044    0.00015 B- -13736#      2000#        3 016029.32197     0.00006
  -3    0    3    3 Li  -pp   28667#       2000#        -2267#       667#      B-      *                    3 030775#       2147#
0  2    3    1    4 H    -n   24621.129     100.000      1720.4491    25.0000  B-  22196.2131   100.0000    4 026431.867     107.354
   0    2    2    4 He         2424.91587     0.00015    7073.9156     0.0002  B- -22898.2740   212.1320    4 002603.25413     0.00016
  -2    1    3    4 Li   -p   25323.190     212.132      1153.7603    53.0330  B-      *                    4 027185.561     227.733
```

- - `nubase_4.mas20.txt`, a 5868 line text file, for half life and decay processes

``` line 26
001 0000   1n       8071.3181     0.0004                              609.8    s 0.6    1/2+*         06          1932 B-=100
001 0010   1H       7288.971064   0.000013                            stbl              1/2+*         06          1920 IS=99.9855 78
002 0010   2H      13135.722895   0.000015                            stbl              1+*           03          1932 IS=0.0145 78
003 0010   3H      14949.81090    0.00008                              12.32   y 0.02   1/2+*         00          1934 B-=100
003 0020   3He     14931.21888    0.00006                             stbl              1/2+*         98          1934 IS=0.0002 2
003 0030   3Li     28670#      2000#                                  p-unst            3/2-#         98               p ?
004 0010   4H      24620        100                                   139     ys 10     2-            98          1981 n=100
004 0020   4He      2424.91587    0.00015                             stbl              0+            98          1908 IS=99.9998 2
004 0030   4Li     25320        210                                    91     ys 9      2-            98          1965 p=100
```

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
- Make the shop depend on your position
- Give the player and built structures inventories
- Buildable structures
- Archive the BoxSim (Volumes will be used in the World)
