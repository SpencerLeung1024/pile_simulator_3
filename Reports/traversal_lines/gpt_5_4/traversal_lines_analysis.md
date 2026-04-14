# TraversalLines Scaling Analysis

## Summary

The current LOD query in `QueryForLOD` is **not** expected to scale like `r^2`, where `r` is the realization radius.

For the code in `DSA/Octree.cs`, the expected scaling is instead:

- **Visited nodes:** `Theta(r^3 * log2(A / r))`
- **Leaf nodes:** `Theta(r^3)`
- **Theta-failed internal nodes:** `Theta(r^3 * log2(A / r))`
- **Far terminus / multimesh candidates:** asymptotically `Theta(r^2 * log2(A / r))`, though over the tested range they look close to `r^2`

Where:

- `r` = realization radius from the UI slider
- `A` = asteroid radius / camera distance scale / far-field extent of the tree

For the asteroid-size sweep at fixed realization radius `r`:

- **If `A <=~ r`**: the traversal degenerates toward full realization, so cost is `Theta(A^3)`
- **If `A >> r`**: the traversal grows like `Theta(r^3 * log2(A / r))`

That is why the measured behavior is much closer to `r^3 log r` than to `r^2`.

---

## What the code actually does

`QueryForLOD` in `DSA/Octree.cs` performs a depth-first traversal from the root.

For each internal node it computes:

```text
nodeTheta = node.Size / distance(camera, node.Center)
```

and then:

- if `nodeTheta < theta`, it **stops descending** and uses that node as a terminus
- otherwise it **realizes/pushes all 8 children**

In `Scripts/Asteroid.cs`, the threshold is:

```text
theta = 1 / realizationRadius
```

so the descent condition is approximately:

```text
node.Size / distance >= 1 / r
distance <= node.Size * r
```

For a node size `s`, the algorithm descends into all nodes whose centers lie within a region of radius proportional to `s * r` around the camera.

---

## Deriving the scaling law

## 1. Cost contributed by one octree level

Take one octree level where node size is `s`.

The algorithm keeps descending for nodes with:

```text
distance <= s * r
```

So at that level, the descended region has volume:

```text
Volume ~ (s * r)^3
```

Each node at that level covers volume:

```text
NodeVolume ~ s^3
```

Therefore the number of level-`s` nodes that get visited at that level is:

```text
(s * r)^3 / s^3 = r^3
```

This is the key point: **each relevant octree level contributes about `Theta(r^3)` visited nodes**.

---

## 2. How many levels are relevant?

The finest level is leaf size `1`.

The coarsest useful level is set by the far extent of the asteroid / tree, on the order of `A`.

Because octree sizes grow by powers of 2, the number of relevant levels is:

```text
Theta(log2(A / r))
```

So total visited work becomes:

```text
Visited ~ r^3 * log2(A / r)
```

This matches the data much better than `r^2`, `r^2 log r`, or plain `r^3`.

---

## 3. Why leaf nodes scale like `r^3`

Leaves correspond to the fully realized near field.

At leaf size `s = 1`, the descended region has radius proportional to `r`, so the number of visited leaves is proportional to the near-field volume:

```text
Leaf ~ r^3
```

This is exactly what the dump shows.

---

## 4. Why far visible nodes are closer to `r^2`

Far nodes that pass theta live near the asteroid surface rather than filling the full volume.

At level `s`, the surface patch affected by the traversal has area roughly:

```text
Area ~ (s * r)^2
```

Each node face covers area `s^2`, so the number of surface nodes at that level is:

```text
(s * r)^2 / s^2 = r^2
```

So each level contributes about `Theta(r^2)` far surface nodes, and summing over levels gives:

```text
FarTerminus ~ r^2 * log2(A / r)
```

In the measured sweep, `log2(A / r)` only changes from about `9.97` at `r=10` to about `7.64` at `r=50`, so this extra log factor is weak enough that the multimesh count looks almost quadratic.

---

## Check against the dump

Base setup from `dump_debug.txt`:

- asteroid radius `A = 10000 m`
- realization radius sweep `r = 10, 20, 30, 40, 50`

Measured values:

| `r` | Visited | Leaf | MultiMesh |
|---:|---:|---:|---:|
| 10 | 359,449 | 33,792 | 8,283 |
| 20 | 2,581,441 | 268,416 | 30,011 |
| 30 | 8,134,729 | 904,832 | 62,410 |
| 40 | 18,396,841 | 2,144,768 | 115,600 |
| 50 | 34,511,169 | 4,191,872 | 176,298 |

### Leaf normalization

`Leaf / r^3` is nearly constant:

| `r` | `Leaf / r^3` |
|---:|---:|
| 10 | 33.792 |
| 20 | 33.552 |
| 30 | 33.512 |
| 40 | 33.512 |
| 50 | 33.535 |

This is an extremely clean `Theta(r^3)` fit.

### Visited normalization

`Visited / (r^3 * log2(A / r))` is also nearly constant:

| `r` | `Visited / (r^3 * log2(10000 / r))` |
|---:|---:|
| 10 | 36.068 |
| 20 | 35.990 |
| 30 | 35.949 |
| 40 | 36.086 |
| 50 | 36.119 |

This is a much tighter fit than any `r^2`-based law.

### Theta-failed normalization

`ThetaFailed / (r^3 * log2(A / r))` is nearly constant too:

| `r` | `ThetaFailed / (r^3 * log2(10000 / r))` |
|---:|---:|
| 10 | 4.509 |
| 20 | 4.499 |
| 30 | 4.494 |
| 40 | 4.511 |
| 50 | 4.515 |

That is exactly what the derivation predicts, because theta-failed nodes are the nodes whose children get expanded, and expansion is the expensive volumetric part.

---

## Asteroid-size sweep interpretation

The asteroid-size sweep keeps `r = 50 m` fixed while changing `A`.

Measured visited counts:

| `A` | Visited | Theta Passed |
|---:|---:|---:|
| 20 | 257,801 | 0 |
| 100 | 6,720,265 | 1,828,899 |
| 500 | 16,383,849 | 10,071,746 |
| 2000 | 24,761,545 | 17,400,134 |
| 10000 | 34,511,169 | 25,922,372 |

This is best understood as a **piecewise law**.

### Small asteroid regime: `A <=~ r`

When the asteroid is no larger than the realization radius, the traversal reaches leaves almost everywhere.

Then the cost behaves like visiting the whole asteroid volume:

```text
Visited ~ A^3
```

The `20 m` case is in this regime. It has `Theta Passed = 0`, which means there is effectively no far-field approximation left.

### Large asteroid regime: `A >> r`

Once the asteroid is much larger than the realization radius, each extra doubling of asteroid size adds one more coarse LOD shell.

So the cost becomes:

```text
Visited ~ r^3 * log2(A / r)
```

With `r = 50` fixed, that means the dependency on asteroid size is only logarithmic.

This matches the trend from `100 -> 500 -> 2000 -> 10000 m`: the growth is much slower than any power law in `A`, and is dominated by the number of additional octree levels outside the realized near field.

---

## What this means for expectations

The current query is volumetric near the camera, not surface-only.

So the correct expectation for the present algorithm is:

- **realization radius sweep:** not `r^2`, but `r^3 * log2(A / r)` for total traversal work
- **asteroid radius sweep:** not `log2(A)` in all regimes, but:
  - `Theta(A^3)` when the asteroid is smaller than the realized near field
  - `Theta(r^3 * log2(A / r))` once the asteroid is much larger than the realized near field

If you want the total query to scale more like `r^2 * log r`, the traversal would need to become fundamentally surface-driven instead of volume-driven. In the current code, the theta-fail region occupies a 3D volume around the camera at every active level, so `r^3` is unavoidable.

---

## Final answer

For the current implementation, the scaling law that best matches both the code and the dump is:

```text
Visited = Theta(r^3 * log2(A / r))
```

with subcomponents:

```text
Leaf = Theta(r^3)
ThetaFailed = Theta(r^3 * log2(A / r))
FarTerminus / MultiMesh = Theta(r^2 * log2(A / r))   (often looks close to Theta(r^2) in practice)
```

and for asteroid-size sweeps at fixed `r`:

```text
Visited = Theta(min(A^3, r^3 * log2(A / r)))
```

interpreted piecewise as:

- `Theta(A^3)` for `A <=~ r`
- `Theta(r^3 * log2(A / r))` for `A >> r`