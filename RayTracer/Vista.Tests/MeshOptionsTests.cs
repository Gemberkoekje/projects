using System.Text.Json.Nodes;
using Vista.Cli;
using Vista.Output;
using Vista.World;

namespace Vista.Tests;

/// <summary>How a request asks for its mesh (vista-plan §10 M1.9): the CLI options, the sheet axes, and the label a sheet gives it.</summary>
[TestClass]
public sealed class MeshOptionsTests
{
    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string Error(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
        return string.Join(" | ", errors);
    }

    [TestMethod]
    public void TheCommandLine_SetsTheMesh()
    {
        Assert.AreEqual(new MeshSettings(), Parse().Mesh, "the view-dependent mesh at 1 px, with the detail and the far field");
        Assert.AreEqual(new MeshSettings { Kind = MeshKind.Uniform, ErrorPixels = 0.5, Detail = false, FarField = false },
            Parse("--mesh", "uniform", "--mesh-error", "0.5", "--detail", "off", "--far-field", "off").Mesh);
        Assert.AreEqual(MeshKind.View, Parse("--mesh", "view").Mesh.Kind);
        StringAssert.Contains(Error("--mesh", "lumpy"), "--mesh must be view or uniform");
        StringAssert.Contains(Error("--mesh-error", "0"), "--mesh-error must be");
        StringAssert.Contains(Error("--mesh-error", "-1"), "--mesh-error must be");
        StringAssert.Contains(Error("--detail", "some"), "--detail must be on or off");
        StringAssert.Contains(Error("--far-field", "1"), "--far-field must be on or off");
    }

    [TestMethod]
    public void ASheet_VariesTheMesh()
    {
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("meshError=0.5,1,2", out SheetAxis? error, out string? problem), problem);
        Assert.AreEqual(2.0, error!.Apply(request, 2).Mesh.ErrorPixels);
        Assert.IsTrue(SheetAxis.TryParse("mesh=uniform,view", out SheetAxis? kind, out problem), problem);
        Assert.AreEqual(MeshKind.Uniform, kind!.Apply(request, 0).Mesh.Kind);
        Assert.IsTrue(SheetAxis.TryParse("detail=off,on", out SheetAxis? detail, out problem), problem);
        Assert.IsFalse(detail!.Apply(request, 0).Mesh.Detail);
        Assert.IsTrue(SheetAxis.TryParse("farField=off,on", out SheetAxis? far, out problem), problem);
        Assert.IsTrue(far!.Apply(request with { Mesh = new MeshSettings { FarField = false } }, 1).Mesh.FarField);
        Assert.IsFalse(SheetAxis.TryParse("meshError=1,big", out _, out problem));
        StringAssert.Contains(problem, "not an error between");
        Assert.IsFalse(SheetAxis.TryParse("mesh=view,flat", out _, out problem));
    }

    [TestMethod]
    public void ASheetFromSidecars_LabelsTheMesh()
    {
        static JsonNode Sidecar(string mesh) => JsonNode.Parse($$"""
            {
              "schema": 10,
              "seed": 42,
              "defaultCamera": true,
              "image": { "width": 320, "height": 180 },
              "world": { "mesh": {{mesh}} }
            }
            """)!;
        Assert.AreEqual("view 1 px", SheetLabels.Identity(Sidecar("""{ "kind": "view", "errorPixels": 1, "detail": true, "farField": true }"""))["mesh"]);
        Assert.AreEqual("view 0.5 px, no detail, no far field",
            SheetLabels.Identity(Sidecar("""{ "kind": "view", "errorPixels": 0.5, "detail": false, "farField": false }"""))["mesh"]);
        Assert.AreEqual("uniform", SheetLabels.Identity(Sidecar("""{ "kind": "uniform", "errorPixels": 0, "detail": false, "farField": false }"""))["mesh"]);
        Assert.IsFalse(SheetLabels.Identity(JsonNode.Parse("""{ "schema": 9, "seed": 1 }""")).ContainsKey("mesh"), "older sidecars have none");
    }
}
