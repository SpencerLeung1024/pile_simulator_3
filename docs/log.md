# Log

## 2026-05-15: Chemistry 101
- Wrote .md files describing how the chemistry system will work
- Filled in the elements
- Started working on other chemistry code but I'm too dumb

## 2026-05-14: Stealing stuff from Stationeers
- I wasted a month playing Stationeers and having my base randomly explode
- Reorganized things (DSA/*, docs)
- Migrated from Roo Code (.clinerules) to OpenCode (AGENTS.md)

## 2026-04-14: Octree and LOD shennanigans
- Octree nodes are shown as large multi meshes when far away and 1 m StaticBodies when within the realization radius
- My initial algorithm scaled as r^3 * log2(a/r)
- GPT-5.4 and Opus 4.6 suggested flood filling from a surface seed

## 2026-04-10: Asteroid generation
- The asteroid is a height deformation from a sphere
- The core is metal, the rest is rock, and any empty space below datum is filled in with ice "oceans" like Minmus

## 2026-04-07: Pile Simulator 3
- Let's try again, with a MultiMesh and manual LOD control

## 2026-04-06: Gridmap Pile Simulator
- GridMap lags after 70 m radius. That won't scale to kilometers, the limit of reasonable floating point behavior and where tunneling into asteroids gets interesting

## 2026-03-09: Shader shennanigans
- Tried to make a shader for plane visualization of the gravitational potential
- Couldn't get it to work

## 2026-03-07: Jolt is too slow
- A rubble pile lags after 8 m radius
- Even turning down the steps and making rocks fly everywhere from instability is not enough
- At least I got some interesting simulations of collisions and moon formation

## 2026-03-06: I can't believe it's not O(n^2)!
- Barnes-Hut approximation for gravity
- Kimi K2.5 used the Task Parallel Library to speed up octree building and semi-implicit Euler

## 2026-03-05: Higher order integrators
- I looked on Wikipedia and implemented the Velocity-Verlet (O(n^2)) and Yoshida (O(n^4)) integrators
- In practice errors from physics launching rocks to infinity are more significant than errors from semi-implicit Euler (O(n))

## 2026-02-27: Rubble Pile Simulator
- No one can afford a house in this day and age so we're going to space
