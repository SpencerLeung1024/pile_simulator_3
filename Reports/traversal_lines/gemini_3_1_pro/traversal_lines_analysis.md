# Traversal Lines Analysis

## Expected Scaling
- **Realization Radius ($r$)**: $O(r^2 \log_2(r))$ - $r^2$ to represent the 2D surface area within the 3D realization volume, and $\log_2(r)$ for octree traversal overhead.
- **Asteroid Size ($a$)**: $O(\log_2(a))$ - Traversing down the octree to the surface should take depth proportional to the tree height, which is $\log_2(a)$.

## Observed Scaling

Based on curve fitting of the `Visited` nodes data from `dump_debug.txt`:

### Sweep Realization Radius (Asteroid Size = 10 km)
| Function | $R^2$ Value |
| :--- | :--- |
| $O(r^3)$ | 1.0000 |
| $O(r^2)$ | 0.9992 |
| $O(r^2 \log_2(r))$ | 0.9933 |

The number of visited nodes scales perfectly with $O(r^3)$. This indicates that the algorithm is visiting the *volume* of the realization sphere, rather than just the *surface*.

### Sweep Asteroid Size (Realization Radius = 50 m)
| Function | $R^2$ Value |
| :--- | :--- |
| $O(\log_2(a))$ | 0.9945 |
| $O(a^2)$ | 0.9258 |
| $O(a \log_2(a))$ | 0.6694 |

The number of visited nodes scales very well with $O(\log_2(a))$, which matches expectations. The overhead of a larger asteroid is primarily just the increased depth of the octree.

## Why is Realization Radius $O(r^3)$?

Looking at the breakdown for the 50m realization radius:
```text
Visited: 34511169
  Leaf: 4191872
    Empty: 2331179 Solid: 1860693
      Enclosed: 1832755 Exposed: 27938
  Internal: 30319297
    Truly Empty: 80589
    Truly Solid: 2440
      Enclosed: 2440 Exposed*: 0
    Theta Passed: 25922372
      Empty: 14924522 Solid: 10997850
        Enclosed: 10839490 Exposed: 158360
```

The vast majority of visited nodes are **Enclosed** (e.g., 10,839,490 Internal Theta Passed Solid Enclosed nodes, and 1,832,755 Leaf Solid Enclosed nodes). 

An "Enclosed" node is one that is completely surrounded by other solid nodes and therefore not visible on the surface. Because the algorithm is visiting millions of enclosed nodes, it is effectively processing the entire solid volume of the asteroid within the realization radius, leading to the $O(r^3)$ scaling.

### The Root Cause
As noted in the current problems:
> "Due to how the asteroid generator works, an LOD internal node B may be !Material.Empty even if there exists children that are Material.Empty"

Because internal nodes cannot be reliably classified as "Truly Solid" (only 2,440 were Truly Solid compared to 10,997,850 that were just "Solid"), the traversal cannot prune the interior of the asteroid early. It is forced to traverse deep into the octree (often all the way to the leaves) just to verify if a region is completely solid or if it contains empty pockets. This prevents the algorithm from culling the $O(r^3)$ interior volume, causing it to visit all nodes within the realization radius instead of just the $O(r^2)$ surface nodes.
