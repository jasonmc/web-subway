#import "brand.typ": canvas, draw, badge, palette
#set page(width: 512pt, height: 512pt, margin: 0pt, fill: palette.navy)
#canvas(length: 1pt, {
  draw.rect((0, 0), (512, 512), fill: palette.navy, stroke: none)
  badge((256, 256), 190)
})
