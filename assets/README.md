# Assets

## icon.ico

The application icon: a 7-size `.ico` (16/24/32/48/64/128/256) generated from
`icon.svg`. Ready to use — no conversion needed.

Colours come from the Academic theme (`#1F3A5F` navy, `#8C6D3F` bronze,
`#F5F3EE` parchment), so the icon matches the app's default appearance.

### Regenerating

Edit `icon.svg`, then:

```bash
pip install cairosvg pillow
python make-icon.py
```

### Design notes

Deliberately low-detail. At 16x16 an icon has ~256 pixels; anything intricate
turns to mud. The checkmark badge overlaps the document's lower-right corner
specifically to break the plain-rectangle silhouette, which is what makes it
identifiable at taskbar size.

`icon.svg` is the source of truth — regenerate the `.ico` rather than editing
it directly.

### Wiring it up

`Directory.Build.props` sets `<ApplicationIcon>` for the App project. It has no
effect until `QuizBuilder.App` exists (later slice); the property is harmless
on the Core and Tests libraries.
