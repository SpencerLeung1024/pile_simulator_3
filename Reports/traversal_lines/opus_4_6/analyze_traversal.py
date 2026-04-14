"""
Analyze TraversalLines scaling from dump_debug.txt

The Barnes-Hut LOD query in Octree.QueryForLOD uses theta = 1/r where r = realization radius.
A node at height h has Size = 2^h. It gets expanded (theta fails) when:
   nodeTheta = Size / distance >= theta
   => 2^h / dist >= 1/r
   => dist <= 2^h * r

We want to understand how Visited nodes scale with r (realization radius) and a (asteroid radius).
"""

import numpy as np
from scipy.optimize import curve_fit
from itertools import combinations

# ============================================================
# DATA EXTRACTION
# ============================================================

# Sweep realization radius (a=10000m, neighbor culling on)
r_sweep = {
    'r': np.array([10, 20, 30, 40, 50], dtype=float),
    'visited': np.array([359449, 2581441, 8134729, 18396841, 34511169], dtype=float),
    'leaf': np.array([33792, 268416, 904832, 2144768, 4191872], dtype=float),
    'internal': np.array([325657, 2313025, 7229897, 16252073, 30319297], dtype=float),
    'theta_passed': np.array([277246, 1977113, 6184371, 13900924, 25922372], dtype=float),
    'theta_failed': np.array([44931, 322680, 1016841, 2299605, 4313896], dtype=float),
    'truly_empty': np.array([3433, 12948, 28221, 50062, 80589], dtype=float),
    'truly_solid': np.array([47, 284, 464, 1482, 2440], dtype=float),
    'octree_ms': np.array([16.21, 124.83, 385.44, 864.43, 1594.98], dtype=float),
    'multimesh': np.array([8283, 30011, 62410, 115600, 176298], dtype=float),
    'static': np.array([956, 4323, 9947, 10000, 10000], dtype=float),
}

# Sweep asteroid size (r=50m, neighbor culling on)
a_sweep = {
    'a': np.array([20, 100, 500, 2000, 10000], dtype=float),
    'visited': np.array([257801, 6720265, 16383849, 24761545, 34511169], dtype=float),
    'leaf': np.array([220288, 3986000, 4189776, 4191872, 4191872], dtype=float),
    'internal': np.array([37513, 2734265, 12194073, 20569673, 30319297], dtype=float),
    'theta_passed': np.array([0, 1828899, 10071746, 17400134, 25922372], dtype=float),
    'theta_failed': np.array([32225, 840033, 2047981, 3095193, 4313896], dtype=float),
    'truly_empty': np.array([5280, 64405, 72786, 72786, 80589], dtype=float),
    'truly_solid': np.array([8, 928, 1560, 1560, 2440], dtype=float),
    'octree_ms': np.array([4.08, 177.99, 659.64, 1100.80, 1594.98], dtype=float),
}

# ============================================================
# FITTING FUNCTIONS
# ============================================================

def power_law(x, a, n):
    """f(x) = a * x^n"""
    return a * np.power(x, n)

def power_law_with_const(x, a, n, c):
    """f(x) = a * x^n + c"""
    return a * np.power(x, n) + c

def log_power(x, a, n, b):
    """f(x) = a * x^n * log2(x) + b"""
    return a * np.power(x, n) * np.log2(x) + b

def fit_power_law(x, y, label=""):
    """Fit y = a * x^n and report results"""
    try:
        popt, pcov = curve_fit(power_law, x, y, p0=[1, 2], maxfev=10000)
        a, n = popt
        y_pred = power_law(x, a, n)
        residuals = y - y_pred
        ss_res = np.sum(residuals**2)
        ss_tot = np.sum((y - np.mean(y))**2)
        r_squared = 1 - ss_res / ss_tot
        return {'a': a, 'n': n, 'r2': r_squared, 'label': label}
    except Exception as e:
        return {'error': str(e), 'label': label}

def fit_power_law_const(x, y, label=""):
    """Fit y = a * x^n + c and report results"""
    try:
        popt, pcov = curve_fit(power_law_with_const, x, y, p0=[1, 2, 0], maxfev=10000)
        a, n, c = popt
        y_pred = power_law_with_const(x, a, n, c)
        residuals = y - y_pred
        ss_res = np.sum(residuals**2)
        ss_tot = np.sum((y - np.mean(y))**2)
        r_squared = 1 - ss_res / ss_tot
        return {'a': a, 'n': n, 'c': c, 'r2': r_squared, 'label': label}
    except Exception as e:
        return {'error': str(e), 'label': label}

def fit_log_power(x, y, label=""):
    """Fit y = a * x^n * log2(x) + b and report results"""
    try:
        popt, pcov = curve_fit(log_power, x, y, p0=[1, 2, 0], maxfev=10000)
        a, n, b = popt
        y_pred = log_power(x, a, n, b)
        residuals = y - y_pred
        ss_res = np.sum(residuals**2)
        ss_tot = np.sum((y - np.mean(y))**2)
        r_squared = 1 - ss_res / ss_tot
        return {'a': a, 'n': n, 'b': b, 'r2': r_squared, 'label': label}
    except Exception as e:
        return {'error': str(e), 'label': label}

def compute_local_exponents(x, y):
    """Compute log-log slopes between consecutive points"""
    slopes = []
    for i in range(len(x) - 1):
        slope = np.log(y[i+1] / y[i]) / np.log(x[i+1] / x[i])
        slopes.append((x[i], x[i+1], slope))
    return slopes

def compute_all_pair_exponents(x, y):
    """Compute log-log slopes between all pairs of points"""
    slopes = []
    for i, j in combinations(range(len(x)), 2):
        slope = np.log(y[j] / y[i]) / np.log(x[j] / x[i])
        slopes.append((x[i], x[j], slope))
    return slopes

# ============================================================
# ANALYSIS
# ============================================================

print("=" * 80)
print("TRAVERSAL LINES SCALING ANALYSIS")
print("=" * 80)

# --- PART 1: Realization Radius Sweep ---
print("\n" + "=" * 80)
print("PART 1: REALIZATION RADIUS SWEEP (asteroid = 10 km)")
print("=" * 80)

print("\nRaw data:")
print(f"  r:           {r_sweep['r']}")
print(f"  Visited:     {r_sweep['visited']}")
print(f"  Leaf:        {r_sweep['leaf']}")
print(f"  ThetaPassed: {r_sweep['theta_passed']}")
print(f"  ThetaFailed: {r_sweep['theta_failed']}")
print(f"  Octree ms:   {r_sweep['octree_ms']}")

# Log-log slopes for Visited vs r
print("\n--- Local log-log slopes (consecutive pairs) for Visited vs r ---")
slopes = compute_local_exponents(r_sweep['r'], r_sweep['visited'])
for x1, x2, s in slopes:
    print(f"  r={x1:.0f} to r={x2:.0f}: slope = {s:.4f}")

print("\n--- All-pair log-log slopes for Visited vs r ---")
slopes = compute_all_pair_exponents(r_sweep['r'], r_sweep['visited'])
for x1, x2, s in slopes:
    print(f"  r={x1:.0f} to r={x2:.0f}: slope = {s:.4f}")

# Fit various models to Visited vs r
print("\n--- Fits for Visited vs r ---")
for metric_name in ['visited', 'leaf', 'internal', 'theta_passed', 'theta_failed', 'truly_empty', 'octree_ms', 'multimesh']:
    y = r_sweep[metric_name]
    print(f"\n  {metric_name}:")
    
    result = fit_power_law(r_sweep['r'], y, f"{metric_name} ~ r^n")
    if 'error' not in result:
        print(f"    Power law: {result['a']:.4e} * r^{result['n']:.4f}  (R²={result['r2']:.6f})")
    
    result2 = fit_power_law_const(r_sweep['r'], y, f"{metric_name} ~ r^n + c")
    if 'error' not in result2:
        print(f"    Power+const: {result2['a']:.4e} * r^{result2['n']:.4f} + {result2['c']:.4e}  (R²={result2['r2']:.6f})")
    
    result3 = fit_log_power(r_sweep['r'], y, f"{metric_name} ~ r^n*log2(r)")
    if 'error' not in result3:
        print(f"    Log-power: {result3['a']:.4e} * r^{result3['n']:.4f} * log2(r) + {result3['b']:.4e}  (R²={result3['r2']:.6f})")
    
    # Also compute local slopes
    local = compute_local_exponents(r_sweep['r'], y)
    slopes_str = ", ".join([f"{s:.3f}" for _, _, s in local])
    print(f"    Local slopes: [{slopes_str}]")

# --- PART 2: Asteroid Size Sweep ---
print("\n\n" + "=" * 80)
print("PART 2: ASTEROID SIZE SWEEP (realization radius = 50 m)")
print("=" * 80)

print("\nRaw data:")
print(f"  a:           {a_sweep['a']}")
print(f"  Visited:     {a_sweep['visited']}")
print(f"  Leaf:        {a_sweep['leaf']}")
print(f"  ThetaPassed: {a_sweep['theta_passed']}")
print(f"  ThetaFailed: {a_sweep['theta_failed']}")
print(f"  Octree ms:   {a_sweep['octree_ms']}")

# The octree height H = ceil(log2(2*a)) for asteroid radius a
H = np.ceil(np.log2(2 * a_sweep['a']))
print(f"  Octree H:    {H}")

print("\n--- Local log-log slopes (consecutive pairs) for Visited vs a ---")
slopes = compute_local_exponents(a_sweep['a'], a_sweep['visited'])
for x1, x2, s in slopes:
    print(f"  a={x1:.0f} to a={x2:.0f}: slope = {s:.4f}")

print("\n--- All-pair log-log slopes for Visited vs a ---")
slopes = compute_all_pair_exponents(a_sweep['a'], a_sweep['visited'])
for x1, x2, s in slopes:
    print(f"  a={x1:.0f} to a={x2:.0f}: slope = {s:.4f}")

# Fit various models to Visited vs a
print("\n--- Fits for Visited vs a ---")
for metric_name in ['visited', 'leaf', 'internal', 'theta_passed', 'theta_failed', 'truly_empty', 'octree_ms']:
    y = a_sweep[metric_name]
    print(f"\n  {metric_name}:")
    
    result = fit_power_law(a_sweep['a'], y, f"{metric_name} ~ a^n")
    if 'error' not in result:
        print(f"    Power law: {result['a']:.4e} * a^{result['n']:.4f}  (R²={result['r2']:.6f})")
    
    result2 = fit_power_law_const(a_sweep['a'], y, f"{metric_name} ~ a^n + c")
    if 'error' not in result2:
        print(f"    Power+const: {result2['a']:.4e} * a^{result2['n']:.4f} + {result2['c']:.4e}  (R²={result2['r2']:.6f})")
    
    result3 = fit_log_power(a_sweep['a'], y, f"{metric_name} ~ a^n*log2(r)")
    if 'error' not in result3:
        print(f"    Log-power: {result3['a']:.4e} * a^{result3['n']:.4f} * log2(a) + {result3['b']:.4e}  (R²={result3['r2']:.6f})")
    
    local = compute_local_exponents(a_sweep['a'], y)
    slopes_str = ", ".join([f"{s:.3f}" for _, _, s in local])
    print(f"    Local slopes: [{slopes_str}]")

# --- PART 3: Theoretical Analysis ---
print("\n\n" + "=" * 80)
print("PART 3: THEORETICAL ANALYSIS")
print("=" * 80)

print("""
The Barnes-Hut LOD traversal works as follows:
  theta = 1/r  (r = realization radius)
  A node at height h has Size = 2^h
  It gets expanded (theta fails) if: 2^h / dist >= 1/r, i.e., dist <= 2^h * r

The octree has height H = ceil(log2(2*a)) for asteroid radius a.

The camera sits on the surface of the asteroid, distance ~a from origin.

KEY INSIGHT: Consider the traversal level by level.
- At height h, nodes have size 2^h
- "Expansion radius" around camera = 2^h * r  
- Nodes at height h that are within distance 2^h * r of camera get expanded into 8 children
- Nodes at height h that are farther than 2^h * r are terminal (theta passed)

The number of nodes at height h within distance d of the camera depends on the geometry:
  - If d << a: essentially flat surface, volume ~ d^3, so nodes ~ d^3 / (2^h)^3 = (d / 2^h)^3
  - If d >> a: the whole asteroid, so nodes are capped at the total nodes at that height

For the "expansion radius" = 2^h * r:
  - Nodes expanded at height h ~ (2^h * r)^3 / (2^h)^3 = r^3  (CONSTANT per level if d << a!)
  
Wait, that gives r^3 per level, times ~H levels = r^3 * log(a).
But each expanded node has 8 children, and the CHILDREN become the "visited internal" nodes at height h-1.
Actually, the total visited is the sum across all levels.

Let me reconsider: in standard Barnes-Hut in 3D, with N particles uniformly distributed,
the traversal cost per particle is O(N * log(N)). But here we're doing a SINGLE query,
not N queries. A single Barnes-Hut traversal visits O(1/theta^3) = O(r^3) nodes for
a uniform 3D distribution. But the asteroid is NOT uniform - it's a 2D surface (shell)
embedded in 3D space.
""")

# Check specific hypotheses for r sweep
print("\n--- Testing specific hypotheses for r sweep (Visited) ---")
r = r_sweep['r']
v = r_sweep['visited']

# r^3
print(f"\n  Visited / r^3:")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {v[i] / r[i]**3:.2f}")

# r^3 * log2(r)
print(f"\n  Visited / (r^3 * log2(r)):")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {v[i] / (r[i]**3 * np.log2(r[i])):.2f}")

# r^2 * log2(r) (user's hope)
print(f"\n  Visited / (r^2 * log2(r)):")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {v[i] / (r[i]**2 * np.log2(r[i])):.2f}")

# r^2
print(f"\n  Visited / r^2:")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {v[i] / r[i]**2:.2f}")

# Also test leafs specifically
print(f"\n  Leaf / r^3:")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {r_sweep['leaf'][i] / r[i]**3:.4f}")

print(f"\n  ThetaFailed / r^3:")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {r_sweep['theta_failed'][i] / r[i]**3:.4f}")

print(f"\n  ThetaPassed / r^3:")
for i in range(len(r)):
    print(f"    r={r[i]:.0f}: {r_sweep['theta_passed'][i] / r[i]**3:.4f}")

# Check specific hypotheses for a sweep
print("\n--- Testing specific hypotheses for a sweep (Visited) ---")
a = a_sweep['a']
v = a_sweep['visited']

print(f"\n  Visited / log2(a):")
for i in range(len(a)):
    print(f"    a={a[i]:.0f}: {v[i] / np.log2(a[i]):.2f}")

print(f"\n  Visited / log2(a)^2:")
for i in range(len(a)):
    print(f"    a={a[i]:.0f}: {v[i] / np.log2(a[i])**2:.2f}")

# Octree height H
print(f"\n  Octree height H = ceil(log2(2a)):")
for i in range(len(a)):
    H_i = np.ceil(np.log2(2 * a[i]))
    print(f"    a={a[i]:.0f}: H={H_i:.0f}, Visited/H={v[i]/H_i:.2f}, Visited/H^2={v[i]/H_i**2:.2f}")

# The key question: after r^3 factor is removed, what's the a dependency?
# Since r=50 is fixed, the base r^3 = 125000
# Visited / r^3: if it scales with some f(a), what is f(a)?
print(f"\n  Visited / 125000 (r^3 with r=50):")
for i in range(len(a)):
    H_i = np.ceil(np.log2(2 * a[i]))
    normalized = v[i] / 125000.0
    print(f"    a={a[i]:.0f}: {normalized:.2f}  (H={H_i:.0f}, /H={normalized/H_i:.2f})")

# --- PART 4: COMPONENT BREAKDOWN ---
print("\n\n" + "=" * 80)
print("PART 4: COMPONENT BREAKDOWN")
print("=" * 80)

print("\nNode count breakdown - r sweep (percentages):")
for i in range(len(r_sweep['r'])):
    ri = r_sweep['r'][i]
    total = r_sweep['visited'][i]
    leaf = r_sweep['leaf'][i]
    tp = r_sweep['theta_passed'][i]
    tf = r_sweep['theta_failed'][i]
    te = r_sweep['truly_empty'][i]
    ts = r_sweep['truly_solid'][i]
    
    print(f"  r={ri:.0f}: Leaf={leaf/total*100:.1f}%, ThetaPassed={tp/total*100:.1f}%, "
          f"ThetaFailed={tf/total*100:.1f}%, TrulyEmpty={te/total*100:.1f}%, TrulySolid={ts/total*100:.1f}%")

print("\nNode count breakdown - a sweep (percentages):")
for i in range(len(a_sweep['a'])):
    ai = a_sweep['a'][i]
    total = a_sweep['visited'][i]
    leaf = a_sweep['leaf'][i]
    tp = a_sweep['theta_passed'][i]
    tf = a_sweep['theta_failed'][i]
    te = a_sweep['truly_empty'][i]
    ts = a_sweep['truly_solid'][i]
    
    print(f"  a={ai:.0f}: Leaf={leaf/total*100:.1f}%, ThetaPassed={tp/total*100:.1f}%, "
          f"ThetaFailed={tf/total*100:.1f}%, TrulyEmpty={te/total*100:.1f}%, TrulySolid={ts/total*100:.1f}%")

# --- PART 5: Relationship between ThetaFailed and expansion ---
print("\n\n" + "=" * 80)
print("PART 5: RELATIONSHIP BETWEEN THETA FAILED AND CHILDREN")
print("=" * 80)

print("""
Each ThetaFailed node expands into 8 children. The total nodes at the next level
come from ThetaFailed * 8 + any TrulySolid that gets re-expanded (Exposed*).

ThetaFailed * 8 should roughly equal Visited - 1 (root), since every visited node
is either the root or a child of a ThetaFailed node (or Exposed TrulySolid node).
""")

for i in range(len(r_sweep['r'])):
    ri = r_sweep['r'][i]
    tf = r_sweep['theta_failed'][i]
    se = 0  # solidExposed is 0 in all r_sweep cases  
    expected_visit = (tf + se) * 8 + 1
    actual = r_sweep['visited'][i]
    print(f"  r={ri:.0f}: ThetaFailed*8+1 = {expected_visit:.0f}, Visited = {actual:.0f}, "
          f"ratio = {actual/expected_visit:.6f}")

# --- PART 6: DEEPER ANALYSIS OF THE r^3 BEHAVIOR ---
print("\n\n" + "=" * 80)
print("PART 6: WHY r^3 AND NOT r^2?")
print("=" * 80)

print("""
The user hoped for r^2 * log(r), reasoning that the surface within the
realization radius is 2D (r^2 surface area), with log(r) overhead.

However, the traversal visits VOLUME, not surface. Here's why:

1. The 'realization radius' is the theta cutoff: theta = 1/r.
   This means ALL nodes within distance 2^h * r get expanded at height h.

2. Near the camera on the asteroid surface, the traversal must drill through
   the ENTIRE VOLUME of the asteroid within the expansion sphere, not just
   the surface. The asteroid is solid below the surface.

3. At height 0 (leaves), the expansion radius = 1*r = r.
   Leaves within distance r of camera: ~r^3 (volume of intersection)
   But half are empty (above surface), so ~r^3/2 leaves.
   This is consistent: Leaf(r=10)=33792, Leaf(r=20)=268416 = 33792*7.94 ≈ 33792*8 = 2^3

4. At height h, expansion radius = 2^h * r.
   Nodes at this level: ~(2^h * r)^3 / (2^h)^3 = r^3
   So EACH LEVEL contributes ~r^3 visited nodes!
   
5. How many levels are there? From h=0 up to some h_max where 2^h * r > 2*a
   (expansion sphere encloses entire asteroid). h_max ≈ log2(2a/r) = log2(a) - log2(r) + 1.
   
6. But wait - at higher levels where the expansion sphere encloses the whole asteroid,
   ALL nodes at that level get expanded, and the count is capped at ~(2a)^3/(2^h)^3.
   
Total: sum over h of min(r^3, (2a/2^h)^3)
""")

# Let's compute the theoretical prediction
print("Theoretical per-level contribution (r sweep, a=10000):")
a_val = 10000.0
for ri in r_sweep['r']:
    print(f"\n  r={ri:.0f}:")
    H = int(np.ceil(np.log2(2 * a_val)))
    total_theory = 0
    for h in range(H + 1):
        expansion_r = (2**h) * ri
        node_size = 2**h
        # Nodes in volume: number of cells of size 2^h within distance expansion_r
        # Volume contribution ~ min((expansion_r)^3, (2*a)^3) / node_size^3
        volume_r3 = ri**3  # = (expansion_r / node_size)^3
        total_cells = (2 * a_val / node_size)**3
        contrib = min(volume_r3, total_cells)
        total_theory += contrib
        if h <= 3 or h >= H - 2 or abs(volume_r3 - total_cells) / max(volume_r3, total_cells) < 0.5:
            cap = "CAP" if volume_r3 > total_cells else ""
            print(f"    h={h:2d}: exp_r={expansion_r:10.0f}, r^3={volume_r3:12.0f}, total_cells={total_cells:12.0f} {cap}")
    # Count levels before cap
    h_cap = np.log2(2 * a_val / ri)
    print(f"    h_cap (where expansion=asteroid) = {h_cap:.2f}")
    print(f"    Levels uncapped: ~{h_cap:.1f}, each contributing r^3={ri**3:.0f}")
    print(f"    Theory total (sum): {total_theory:.0f}")
    print(f"    Actual Visited: {r_sweep['visited'][int(np.where(r_sweep['r'] == ri)[0][0])]:.0f}")

# Final clean hypothesis test: Visited ≈ C * r^3 * log2(a/r)
print("\n\n--- Testing Visited ≈ C * r^3 * log2(2a/r) ---")
print("\n  r sweep (a=10000):")
for i in range(len(r_sweep['r'])):
    ri = r_sweep['r'][i]
    v = r_sweep['visited'][i]
    factor = ri**3 * np.log2(2 * 10000 / ri)
    print(f"    r={ri:.0f}: Visited/{{'r^3 * log2(2a/r)'}} = {v/factor:.4f}")

print("\n  a sweep (r=50):")
for i in range(len(a_sweep['a'])):
    ai = a_sweep['a'][i]
    v = a_sweep['visited'][i]
    factor = 50**3 * np.log2(max(2 * ai / 50, 2))  # avoid log(<=0)
    print(f"    a={ai:.0f}: Visited/{{'r^3 * log2(2a/r)'}} = {v/factor:.4f}")

# But when a=20 and r=50, 2a/r = 0.8 < 1, so the entire asteroid fits within r
# In that case: the total is just the whole tree ~ 8^H
print(f"\n  NOTE: a=20, r=50 is a degenerate case where the entire asteroid fits within the realization radius")
print(f"    All nodes are visited: 257801 out of a tree with H={np.ceil(np.log2(40)):.0f}")
# Sum of 8^h for h=0..H
H20 = int(np.ceil(np.log2(40)))
total_nodes_tree = sum(8**h for h in range(H20 + 1))
print(f"    Complete octree (H={H20}): {total_nodes_tree} nodes")
# But with truly empty/solid pruning, many are skipped
print(f"    But pruning (truly empty/solid) removes many, so actual = 257801")

# More refined: Visited ≈ C * r^3 * (number of uncapped levels)
# Number of uncapped levels = max(0, log2(2a/r))
print("\n\n--- Final refined model: Visited ≈ C * r^3 * max(1, log2(2a/r)) ---")
# Fit C from the data  
# Use r sweep to find C
C_estimates = []
for i in range(len(r_sweep['r'])):
    ri = r_sweep['r'][i]
    v = r_sweep['visited'][i]
    h_uncap = max(1, np.log2(2 * 10000 / ri))
    C_est = v / (ri**3 * h_uncap)
    C_estimates.append(C_est)
    print(f"  r={ri:.0f}: C = {C_est:.4f}")

print(f"\n  Mean C from r sweep: {np.mean(C_estimates):.4f}")
print(f"  Std C from r sweep: {np.std(C_estimates):.4f}")

# Test with a sweep
C_mean = np.mean(C_estimates)
print(f"\n  Predictions for a sweep using C={C_mean:.4f}:")
for i in range(len(a_sweep['a'])):
    ai = a_sweep['a'][i]
    v_actual = a_sweep['visited'][i]
    h_uncap = max(1, np.log2(max(2 * ai / 50, 2)))
    v_pred = C_mean * 50**3 * h_uncap
    print(f"    a={ai:.0f}: predicted={v_pred:.0f}, actual={v_actual:.0f}, ratio={v_actual/v_pred:.3f}")

# --- PART 7: Additional checks for the capped levels contribution ---
print("\n\n" + "=" * 80)
print("PART 7: CHECKING CAPPED LEVELS CONTRIBUTION (a sweep)")
print("=" * 80)

print("""
For the a sweep, when a is small relative to r=50:
  - Some levels have expansion radius > asteroid diameter.
  - At these levels, ALL nodes are visited (capped at total_cells).
  - total_cells at height h = (2a / 2^h)^3
  
For a=20, ALL levels are capped (r=50 > 2a=40).
For a=100, cap starts at h ~= log2(2*100/50) = log2(4) = 2, so levels 0,1 are uncapped.
For a=500, cap starts at h ~= log2(2*500/50) = log2(20) ≈ 4.3
For a=2000, cap starts at h ~= log2(2*2000/50) = log2(80) ≈ 6.3
For a=10000, cap starts at h ~= log2(2*10000/50) = log2(400) ≈ 8.6
""")

for i in range(len(a_sweep['a'])):
    ai = a_sweep['a'][i]
    H_i = int(np.ceil(np.log2(2 * ai)))
    h_cap = np.log2(2 * ai / 50)
    r = 50.0
    total = 0
    uncapped = 0
    capped = 0
    for h in range(H_i + 1):
        node_size = 2**h
        vol_r3 = r**3
        total_cells = max(1, (2 * ai / node_size)**3)
        contrib = min(vol_r3, total_cells)
        if vol_r3 <= total_cells:
            uncapped += contrib
        else:
            capped += contrib
        total += contrib
    print(f"  a={ai:.0f}: H={H_i}, h_cap={h_cap:.2f}, "
          f"total_theory={total:.0f}, uncapped={uncapped:.0f}, capped={capped:.0f}, "
          f"actual={a_sweep['visited'][i]:.0f}")

print("\n\nDONE.")
