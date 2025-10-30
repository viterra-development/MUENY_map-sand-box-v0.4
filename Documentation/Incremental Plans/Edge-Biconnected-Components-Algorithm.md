# Edge-Biconnected Components Algorithm (Tarjan's with Edge Tracking)

## Problem Statement

Given a primal graph where:
- **Vertices** = road intersections
- **Edges** = road segments connecting intersections

Find all **edge-biconnected components** (groups of segments that remain connected after removing any single vertex).

## Key Insight

Unlike vertex-biconnected components (which output sets of vertices), we need **edge-biconnected components** (sets of edges/segments) because:
1. We want to identify groups of road segments that form pendant clusters
2. Assigning segments after the fact based on vertex membership fails in dense networks
3. Most segments touch the main component's vertices, causing incorrect assignment

## Algorithm: Tarjan's DFS with Edge Stack

### Data Structures

```
Graph:
  - Adjacency: Dictionary<string intersectionId, List<string> neighbors>
  - Edges: Dictionary<string segmentId, (string startId, string endId)>
  - SegmentsBetween: Dictionary<(string, string), List<string> segmentIds>

DFS State:
  - visited: HashSet<string> intersections visited
  - discoveryTime: Dictionary<string intersectionId, int>
  - lowLink: Dictionary<string intersectionId, int>
  - parent: Dictionary<string intersectionId, string>
  - time: int (global counter)
  - edgeStack: Stack<string segmentId>
  - components: List<BiconnectedComponent>
  - articulationPoints: HashSet<string>
```

### Main Algorithm

```
FindEdgeBiconnectedComponents(graph):
    Initialize all data structures

    For each unvisited intersection v in graph:
        DFS(v, null)

    Return (articulationPoints, components)
```

### DFS Traversal with Edge Tracking

```
DFS(u, parentEdge):
    visited.Add(u)
    discoveryTime[u] = lowLink[u] = time++
    childCount = 0
    isArticulationPoint = false

    For each neighbor v of u:
        // Get all segments connecting u -> v
        segments = GetSegmentsBetween(u, v)

        For each segment in segments:
            // Skip the edge we came from (for undirected graph)
            If segment == parentEdge:
                Continue

            // PUSH EDGE TO STACK (KEY DIFFERENCE)
            edgeStack.Push(segment)

            If v not visited:
                parent[v] = u
                childCount++

                DFS(v, segment)

                // Update low-link value
                lowLink[u] = Min(lowLink[u], lowLink[v])

                // Check if u is articulation point
                If (parent[u] == null AND childCount > 1) OR
                   (parent[u] != null AND lowLink[v] >= discoveryTime[u]):

                    isArticulationPoint = true

                    // POP COMPONENT FROM STACK
                    component = new BiconnectedComponent()

                    Do:
                        edge = edgeStack.Pop()
                        component.Segments.Add(edge)

                        // Track which intersections bound this component
                        (start, end) = graph.Edges[edge]
                        component.Intersections.Add(start)
                        component.Intersections.Add(end)

                    While edge != segment  // Pop until we reach the edge that started this component

                    components.Add(component)

            Else If v != parent[u]:
                // Back edge - update low-link
                lowLink[u] = Min(lowLink[u], discoveryTime[v])

    If isArticulationPoint:
        articulationPoints.Add(u)
```

### Key Differences from Vertex-Based Approach

| Aspect | Vertex-Based (Wrong) | Edge-Based (Correct) |
|--------|---------------------|---------------------|
| **Stack Contents** | Vertices | **Edges (segments)** |
| **Component Output** | Set of intersections | **Set of road segments** |
| **Assignment** | After DFS (error-prone) | **During DFS (precise)** |
| **Result** | 6,093/6,271 segments in one component | Proper distribution across components |

### Helper Function

```
GetSegmentsBetween(u, v):
    // Return all segments connecting intersection u to intersection v
    // May be multiple parallel segments on same road

    key = (Min(u, v), Max(u, v))  // Normalize for undirected
    Return graph.SegmentsBetween.GetValueOrDefault(key, empty list)
```

## Expected Results

For Parker County road network:
- **Before (broken)**: 6,093 segments in main component, ~178 in others
- **After (correct)**: Segments properly distributed across ~5,000+ components
- **Oak Dr**: Should be part of a small pendant cluster (10-50 segments)
- **Pendant clusters**: Should have reasonable segment counts (1-100 typically)

## Implementation Notes

1. **Undirected graph**: Each road segment appears as two directed edges in adjacency list
2. **Parent edge tracking**: Must skip the edge we came from to avoid revisiting
3. **Multiple segments**: Two intersections may have multiple road segments between them
4. **Component boundaries**: Intersections that appear in components may be articulation points

## Validation Checks

After implementation, verify:
- [ ] No component has more than ~500 segments (indicates over-assignment)
- [ ] Sum of all component segments >= total segments (segments can appear in multiple components at articulation points)
- [ ] Articulation points found match known bottlenecks
- [ ] Oak Dr appears in a pendant cluster with EntrySegs > 0

## References

- Tarjan, R. E. (1972). "Depth-first search and linear graph algorithms"
- Hopcroft, J., & Tarjan, R. (1973). "Algorithm 447: efficient algorithms for graph manipulation"
