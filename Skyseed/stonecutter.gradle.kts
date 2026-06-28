plugins {
    id("dev.kikugie.stonecutter")
}
stonecutter active "1.21.1"

// Chiseled aggregates fan a task out across EVERY version node, so CI (and a local "build everything") can cover all
// versions in one invocation instead of one node at a time. stonecutter.tasks.named(<task>) returns that task per node;
// the aggregate just depends on all of them, so adding a version node needs no edit here. See REFACTORPLAN Stage 3.
tasks.register("chiseledBuild") {
    group = "project"
    description = "Runs 'build' on every Stonecutter version node."
    dependsOn(stonecutter.tasks.named("build").map { it.values })
}
tasks.register("chiseledRunGameTestServer") {
    group = "project"
    description = "Runs 'runGameTestServer' (the gametests) on every version node."
    dependsOn(stonecutter.tasks.named("runGameTestServer").map { it.values })
}
