using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;

namespace CodePublisher.Features.GooglePlay.Releases;

public static class GooglePlayReleaseValidation
{
    public const int MaximumReleaseNoteLength = 500;

    public static string? ValidateNotes(IReadOnlyList<GooglePlayReleaseNote> notes)
    {
        foreach (var note in notes)
        {
            if (string.IsNullOrWhiteSpace(note.Language))
            {
                return "Add a language for each release note.";
            }

            if (string.IsNullOrWhiteSpace(note.Text))
            {
                return "Add text for each release note, or leave the notes empty.";
            }

            if (note.Text.Length > MaximumReleaseNoteLength)
            {
                return $"Release notes must be {MaximumReleaseNoteLength} characters or fewer per language.";
            }
        }

        return null;
    }

    public static string? ValidateNotes(string language, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : ValidateNotes([new GooglePlayReleaseNote(language, text)]);
}
