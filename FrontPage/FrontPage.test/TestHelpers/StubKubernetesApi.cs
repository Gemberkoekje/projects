using System.Net;
using System.Net.Http;
using System.Text;

namespace FrontPage.test.TestHelpers;

/// <summary>
/// Stands in for the Kubernetes API: answers each path with canned JSON, or with an error status
/// for the paths a test breaks.
/// </summary>
internal sealed class StubKubernetesApi : DelegatingHandler
{
    public const string Nodes = "/api/v1/nodes";
    public const string Pods = "/api/v1/pods";
    public const string Deployments = "/apis/apps/v1/deployments";
    public const string StatefulSets = "/apis/apps/v1/statefulsets";
    public const string NodeMetrics = "/apis/metrics.k8s.io/v1beta1/nodes";
    public const string FluxGitRepository = "/apis/source.toolkit.fluxcd.io/v1/namespaces/flux-system/gitrepositories/flux-system";

    private readonly Dictionary<string, string> answers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Nodes] = """
            {"items":[
              {"metadata":{"name":"gembernode-02"},"status":{
                "conditions":[{"type":"MemoryPressure","status":"False"},{"type":"Ready","status":"True"}],
                "allocatable":{"cpu":"4","memory":"8Gi","pods":"110"},
                "nodeInfo":{"kubeletVersion":"v1.37.2+k3s1"}}},
              {"metadata":{"name":"gembernode-03"},"status":{
                "conditions":[{"type":"Ready","status":"Unknown"}],
                "allocatable":{"cpu":"4","memory":"8Gi"},
                "nodeInfo":{"kubeletVersion":"v1.37.2+k3s1"}}}
            ]}
            """,
        [Pods] = """
            {"items":[
              {"metadata":{"name":"kluskat-1","namespace":"kluskat"}},
              {"metadata":{"name":"jellyfin-1","namespace":"zeef"}}
            ]}
            """,
        [Deployments] = """
            {"items":[
              {"metadata":{"name":"kluskat","namespace":"kluskat"},"status":{"readyReplicas":1}},
              {"metadata":{"name":"jellyfin","namespace":"zeef"},"status":{}}
            ]}
            """,
        [StatefulSets] = """
            {"items":[
              {"metadata":{"name":"postgresql","namespace":"flux-system"},"status":{"replicas":1,"readyReplicas":1}}
            ]}
            """,
        [NodeMetrics] = """
            {"items":[
              {"metadata":{"name":"gembernode-02"},"timestamp":"2026-11-12T10:00:00Z","window":"20s","usage":{"cpu":"513m","memory":"2Gi"}}
            ]}
            """,
        [FluxGitRepository] = """
            {"status":{"artifact":{"revision":"main@sha1:6969711fe18fc30117c18c8f371b490dec7d224c","lastUpdateTime":"2026-11-12T03:34:54Z"}}}
            """,
    };

    private readonly HashSet<string> broken = new HashSet<string>(StringComparer.Ordinal);

    public List<string> Requests { get; } = [];

    public void Break(string path) => broken.Add(path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The client asks for node metrics as a custom object with an empty name: ".../nodes/".
        var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
        Requests.Add(request.RequestUri.PathAndQuery);

        var response = broken.Contains(path) || !answers.TryGetValue(path, out var json)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        // The client reads the request back from the response, as a real HttpClientHandler sets it.
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
