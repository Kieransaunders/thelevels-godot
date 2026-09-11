using TheLevels.Core.Simulation;
using NUnit.Framework;

namespace TheLevels.Tests;

public class ScaffoldTests
{
    [Test]
    public void SimulationConfig_GridGeometryMatchesUnitySource()
    {
        Assert.That(SimulationConfig.GridSize, Is.EqualTo(129));
        Assert.That(SimulationConfig.CellSize, Is.EqualTo(96f / 128f).Within(0.0001f));
    }
}
