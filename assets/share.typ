#import "brand.typ": canvas, draw, badge, label, palette
#set page(width: 1200pt, height: 630pt, margin: 0pt, fill: palette.navy)
#canvas(length: 1pt, {
  import draw: *
  rect((0, 0), (1200, 630), fill: palette.navy, stroke: none)

  for x in (740, 860, 980, 1100) {
    line((x, 1), (x, 629), stroke: palette.grid + 2pt)
  }
  for y in (160, 280, 400, 520) {
    line((650, y), (1199, y), stroke: palette.grid + 2pt)
  }

  let route-stroke = (paint: palette.route, thickness: 14pt, cap: "round")
  line((1080, 560), (1080, 500), stroke: route-stroke)
  bezier((1080, 500), (880, 200), (1080, 380), (880, 320), stroke: route-stroke)
  line((880, 200), (880, 50), stroke: route-stroke)
  for station in ((1080, 550), (980, 350), (880, 180), (880, 50)) {
    circle(station, radius: 16, fill: palette.navy, stroke: palette.route + 8pt)
  }

  badge((144, 466), 64)
  content((80, 300), anchor: "base-west", label([G train arrivals], size: 64pt, weight: "bold"))
  content((80, 235), anchor: "base-west", label([Next trains. Both directions.], fill: palette.muted))
  content((80, 103), anchor: "base-west", label(
    [Live arrivals · Nearby stations · Train map], size: 23pt, weight: "medium", fill: palette.accent,
  ))
})
