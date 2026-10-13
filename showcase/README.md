# AutoLUT results showcase

Static before/after/dolphin gallery, deployed to GitHub Pages at
`/showcase/` alongside the WebAssembly app. "Dolphin" = an emulator screenshot
of the same subject, used as the color-accuracy reference. It is optional.

## Layout

```
showcase/
  index.html        edit the USERS array near the top of the <script>
  <username>/
    manifest.json   lists that user's subjects
    <image files>   referenced by manifest.json (any extension)
```

Directory listing does not exist on static hosting, so each user folder needs a
`manifest.json`. That file is the only index of what images exist.

## Add a user

1. Add the username to the `USERS` array in `index.html`.
2. Create a folder `showcase/<username>/`.
3. Add a `manifest.json` (copy an existing one) and paste the images.

## Add images to a user

Drop the files into the user's folder, then add an entry to their
`manifest.json`:

```json
{
  "subjects": [
    {
      "label": "Coral reef",
      "before": "reef-before.png",
      "after": "reef-after.png",
      "dolphin": "reef-dolphin.png"
    }
  ]
}
```

- Filenames are relative to the user folder. Any extension works (png/jpg/webp).
- `dolphin` is optional; omit the key and its toggle is hidden for that subject.
  Same applies to `before`/`after`.

## Local preview

Opening `index.html` from disk (`file://`) will not load users - browsers block
`fetch()` of `manifest.json` from the filesystem. Serve it instead:

```
cd showcase
python -m http.server 8000
```

Then open http://localhost:8000/.

## Deploy

`.github/workflows/pages.yml` copies this folder into the Pages artifact. It
redeploys on release, manual dispatch, or any push touching `showcase/**`.
Live at `https://<user>.github.io/AutoLUT/showcase/`.
