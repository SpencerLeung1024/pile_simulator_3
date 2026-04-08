My plan was to make a game where you move around an asteroid, dig for good stuff, and launch rocks everywhere.

Rubble Pile Simulator uses RigidBody3D for each rock. Even with all the Jolt physics settings turned down to the fewest steps, it had a limit of a few hundred rocks (~10m diameter) for playable frame rates. At a few thousand rocks (~20 m diameter), it was running at like 2 FPS and rocks were phasing into each other and being ejected from the system due to physics instability. Most of the frame time is spent on physics collisions instead of my code that used the Barnes-Hut algorithm for n-body gravity. The vast majority of rocks in the world would need to be static.

Gridmap Pile Simulator used a GridMap. At a few million rocks (~80 m diameter), generating the gridmap took a few minutes on startup. There was also noticeable lag in the render time. This won't work either.

Pile Simulator 3 will require manual control of when to show real physically simulated rocks that you can dislodge and move around, real-size static rocks, and increasingly distant and large approximations of the rest of the asteroid. Decisions to load and unload chunks will need to be done during gameplay as the player moves around.
