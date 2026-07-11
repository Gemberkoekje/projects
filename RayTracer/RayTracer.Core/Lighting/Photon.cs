using System.Numerics;

namespace RayTracer;

/// <summary>
/// A caustic photon: a packet of light energy deposited on a diffuse surface after at least one
/// specular (dielectric / bubble / mirror) interaction (shadows-and-caustics-plan §B). Only such
/// light–specular–diffuse paths are stored — ordinary direct/indirect diffuse light is handled by
/// NEE, so the photon map adds exactly the caustics a backward path tracer cannot connect.
///
/// Like every ray in this spectral engine the photon carries a single <see cref="Wavelength"/>, so
/// dispersion through a prism lands different wavelengths at different positions — a continuous
/// rainbow caustic that an RGB renderer cannot produce.
/// </summary>
/// <param name="Position">World-space landing point on the diffuse receiver.</param>
/// <param name="Normal">Receiver surface normal, used to reject photons on differently-oriented
/// surfaces during the density gather.</param>
/// <param name="Power">Scalar radiant power carried at <see cref="Wavelength"/> (already reduced by
/// any mirror reflectance and Beer–Lambert absorption along the way).</param>
/// <param name="Wavelength">The photon's wavelength in nanometres.</param>
public readonly record struct Photon(Vector3 Position, Vector3 Normal, float Power, int Wavelength);
