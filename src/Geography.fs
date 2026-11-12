module Geography

// Both are decimal degrees, but distinct roles prevent swapping the axes.
[<Measure>]
type latitude

[<Measure>]
type longitude

type Coordinates =
    { latitude: float<latitude>
      longitude: float<longitude> }
