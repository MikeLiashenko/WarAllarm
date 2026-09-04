// Loads the Reader's `secrets.local.txt` (ai_api_key, ai_model, …) on
// platforms with a file system; returns an empty map on web.
// Picks the real implementation via conditional import.
export 'secrets_loader_none.dart'
    if (dart.library.io) 'secrets_loader_io.dart';
