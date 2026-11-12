#import "@preview/cetz:0.5.2": canvas, draw

#let palette = (
  navy: rgb("#0d1522"),
  green: rgb("#15803d"),
  route: rgb("#4ade80"),
  grid: rgb("#202b3a"),
  text: rgb("#e2e8f0"),
  muted: rgb("#aebed2"),
  accent: rgb("#86efac"),
)

// Cap-height bounds center the visible letter rather than its line box.
#let label(body, size: 30pt, weight: "regular", fill: palette.text) = text(
  font: "Inter", size: size, weight: weight, fill: fill,
  top-edge: "cap-height", bottom-edge: "baseline", body,
)

#let badge(center, radius) = {
  draw.circle(center, radius: radius, fill: palette.green, stroke: none)
  draw.content(center, label([G], size: radius * 1.7pt, weight: "bold", fill: white))
}
