import numpy as np
from scipy.optimize import curve_fit
import matplotlib.pyplot as plt

# Data
# Sweep realization radius (Asteroid size = 10 km)
r_realization = np.array([10, 20, 30, 40, 50])
t_octree_r = np.array([16.21, 124.83, 385.44, 864.43, 1594.98])
visited_r = np.array([359449, 2581441, 8134729, 18396841, 34511169])

# Sweep asteroid size (Realization radius = 50 m)
r_asteroid = np.array([20, 100, 500, 2000, 10000])
t_octree_a = np.array([4.08, 177.99, 659.64, 1100.80, 1594.98])
visited_a = np.array([257801, 6720265, 16383849, 24761545, 34511169])

# Fitting functions
def poly2(x, a, b, c):
    return a * x**2 + b * x + c

def poly3(x, a, b, c, d):
    return a * x**3 + b * x**2 + c * x + d

def r2_log2r(x, a, b):
    return a * x**2 * np.log2(x) + b

def log2a(x, a, b):
    return a * np.log2(x) + b

def a_log2a(x, a, b):
    return a * x * np.log2(x) + b

print("--- Sweep Realization Radius (Visited Nodes) ---")
popt, _ = curve_fit(poly2, r_realization, visited_r)
print(f"O(r^2): {popt[0]:.2e} * r^2 + {popt[1]:.2e} * r + {popt[2]:.2e}")
popt, _ = curve_fit(poly3, r_realization, visited_r)
print(f"O(r^3): {popt[0]:.2e} * r^3 + {popt[1]:.2e} * r^2 + {popt[2]:.2e} * r + {popt[3]:.2e}")
popt, _ = curve_fit(r2_log2r, r_realization, visited_r)
print(f"O(r^2 log2(r)): {popt[0]:.2e} * r^2 log2(r) + {popt[1]:.2e}")

print("\n--- Sweep Asteroid Size (Visited Nodes) ---")
popt, _ = curve_fit(log2a, r_asteroid, visited_a)
print(f"O(log2(a)): {popt[0]:.2e} * log2(a) + {popt[1]:.2e}")
popt, _ = curve_fit(poly2, r_asteroid, visited_a)
print(f"O(a^2): {popt[0]:.2e} * a^2 + {popt[1]:.2e} * a + {popt[2]:.2e}")
popt, _ = curve_fit(a_log2a, r_asteroid, visited_a)
print(f"O(a log2(a)): {popt[0]:.2e} * a log2(a) + {popt[1]:.2e}")

# Let's calculate R^2 for each fit
def r_squared(y_true, y_pred):
    ss_res = np.sum((y_true - y_pred)**2)
    ss_tot = np.sum((y_true - np.mean(y_true))**2)
    return 1 - (ss_res / ss_tot)

print("\n--- R^2 Values ---")
print("Realization Radius:")
print(f"O(r^2): {r_squared(visited_r, poly2(r_realization, *curve_fit(poly2, r_realization, visited_r)[0])):.4f}")
print(f"O(r^3): {r_squared(visited_r, poly3(r_realization, *curve_fit(poly3, r_realization, visited_r)[0])):.4f}")
print(f"O(r^2 log2(r)): {r_squared(visited_r, r2_log2r(r_realization, *curve_fit(r2_log2r, r_realization, visited_r)[0])):.4f}")

print("\nAsteroid Size:")
print(f"O(log2(a)): {r_squared(visited_a, log2a(r_asteroid, *curve_fit(log2a, r_asteroid, visited_a)[0])):.4f}")
print(f"O(a^2): {r_squared(visited_a, poly2(r_asteroid, *curve_fit(poly2, r_asteroid, visited_a)[0])):.4f}")
print(f"O(a log2(a)): {r_squared(visited_a, a_log2a(r_asteroid, *curve_fit(a_log2a, r_asteroid, visited_a)[0])):.4f}")
