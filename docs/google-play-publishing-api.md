# Google Play publishing API: what the app can do

Reviewed: 2026-11-20 against the published Google Play Developer API v3 reference and Play Console release guidance.

This document separates what the API exposes from what this app actually does. A "Console only" row means the API cannot do it for a normal app, so the app offers guidance rather than a command that would fail.

## Verified API capabilities

| Capability | API surface | App support |
| --- | --- | --- |
| Upload an AAB | `edits.bundles.upload` (resumable upload) | Yes. The publish flow uploads the selected `.aab` into a fresh edit. |
| Assign a release to a track | `edits.tracks.update` | Yes. Release name, version codes, status, staged fraction and release notes are sent. |
| Release notes | `releaseNotes[]` on a track `Release`, each `{language, text}` | Yes. The Release card takes a language and the notes text. |
| Halt a release (pause serving) | `status: halted` in a track release | Yes. "Halt this release" sets it. A first release cannot be halted because there is no earlier release to fall back to. |
| Remove a release from a testing line | `edits.tracks.update` with an empty `releases[]` | Yes. Testers then fall back to production, provided that version code is high enough. |
| Change a staged rollout | `userFraction` with status `inProgress` or `halted` | Yes. Production and testing tracks support an explicit staged choice; new testing releases default to full rollout. The first production release is always 100%. |
| Promote a release between lines | Same version codes written to the target track | Yes. The bundle is not uploaded again. The app uses the live release's name, notes, status, fraction and version codes, not the next release's editable draft. Multi-release targets are excluded. Only first-production promotion forces completed 100%; other promotions preserve live status and fraction. Production and open-testing promotions require confirmation. |
| Read review state | `applications.tracks.releases.list` | Yes. The app matches `activeArtifacts[].versionCode` from the committed upload, not a non-unique release name. A committed change is not described as "in review" until this status check reports it. |
| Manage several active releases | Track `releases[]` | The app requires exactly one unambiguous live release for halt, remove, promote and rollout changes. It directs multi-release tracks to Play Console instead of overwriting arbitrary entries. |
| Account and project changes | Loaded track ownership | Tracks are cleared and reloaded for the selected account and package. Responses from an older account/package are discarded, and account changes during busy work queue one reload. Management stays disabled until the current pair has loaded successfully. Disconnect removes only the account it revoked. |
| Store images | `edits.images.upload`, `list`, `delete` | Yes. Selected images upload in the same edit as the AAB, before validate and commit. |
| Store text (title, descriptions) | `edits.listings.update` | Not exposed. The app does not edit marketing copy; use Play Console. |
| Upload key reset / app signing key upgrade | Console for standard Play App Signing | Console only. `appsigning.rotateAppSigningKey` accepts only a self-hosted Cloud KMS key plus proof-of-rotation; it does not apply to Google-managed keys. The app opens the official guide. |

## Store images

`edits.images.upload` accepts nine `imageType` values: `phoneScreenshots`, `sevenInchScreenshots`, `tenInchScreenshots`, `tvScreenshots`, `wearScreenshots`, `icon`, `featureGraphic`, `tvBanner`, and `appImageTypeUnspecified` (do not use). The app exposes the eight usable values.

The endpoint is a media upload:

```text
POST https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/{packageName}/edits/{editId}/listings/{language}/{imageType}?uploadType=media
```

The language is a BCP-47 tag. Uploading for a language the app does not support is a no-op. Uploads append to the list for that language and image type.

Documented file requirements:

- Screenshots: JPEG or 24-bit PNG (no alpha), 320 to 3840 px, at most 8 per device type. Large-screen guidance asks for 1080 px or above.
- Store icon: 32-bit PNG with alpha, 512 x 512 px, at most 1 MB.
- Feature graphic: JPEG or 24-bit PNG (no alpha), 1024 x 500 px.

`GooglePlayStoreImageCatalog` checks the extension, nonempty file, file size limit and PNG-only icon rule locally. Pixel dimensions are left to Play, which validates them against the listing. An image that Play rejects surfaces the Google error with the app's normal error explanation.

## Why key upgrade is a link, not a button

The official method description for `appsigning.rotateAppSigningKey` says: "This method only applies to apps enrolled with self-hosted Cloud KMS keys. For apps using standard Google-managed Play App Signing, key rotation requests must be initiated through the Google Play Console UI." The request also needs a `RotatedCloudKmsKey` and a base64 proof-of-rotation lineage. None of that exists for the typical upload-key setup this app supports, so exposing the endpoint would produce a guaranteed failure. The Release card opens the Play App Signing help article instead.

## Verification

```bash
dotnet build CodePublisher.slnx --configuration Release --no-restore
dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release -- --disable-logo --progress off
```

`GooglePlayStoreImageTests` uses a recording `HttpMessageHandler`. It proves the upload URL, media type and ordering against the edit's validate step without calling Google. `GooglePlayApiServiceContractTests` covers live metadata, duplicate-name status matching, and empty-note commit results. `GooglePlayViewModelTests` covers account switching, multi-release guards, first-production defaults, and session clearing. No test signs in, uploads to a live app, or changes a store listing.

## Sources

- [edits.images.upload](https://developers.google.com/android-publisher/api-ref/rest/v3/edits.images/upload)
- [REST resource edits.images](https://developers.google.com/android-publisher/api-ref/rest/v3/edits.images)
- [AppImageType](https://developers.google.com/android-publisher/api-ref/rest/v3/AppImageType)
- [Track and Release resources](https://developers.google.com/android-publisher/api-ref/rest/v3/edits.tracks#Release)
- [appsigning.rotateAppSigningKey](https://developers.google.com/android-publisher/api-ref/rest/v3/appsigning/rotateAppSigningKey)
- [Add preview assets to showcase your app](https://support.google.com/googleplay/android-developer/answer/9866151)
- [Use Play App Signing](https://support.google.com/googleplay/android-developer/answer/9842756)
