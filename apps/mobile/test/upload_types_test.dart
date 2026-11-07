import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/files/files_screen.dart';

void main() {
  test('every extension the file picker offers uploads with a real type', () {
    for (final extension in uploadableFileExtensions) {
      expect(
        uploadContentTypeFor('file.$extension'),
        isNot('application/octet-stream'),
        reason: '.$extension is offered but would upload as a generic file',
      );
    }
  });

  test('Word, PowerPoint and the new text formats map to their types', () {
    expect(
      uploadContentTypeFor('Plan.DOCX'),
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    );
    expect(
      uploadContentTypeFor('deck.pptx'),
      'application/vnd.openxmlformats-officedocument.presentationml.presentation',
    );
    expect(uploadContentTypeFor('rota.tsv'), 'text/tab-separated-values');
    expect(uploadContentTypeFor('opening.ics'), 'text/calendar');
    expect(uploadContentTypeFor('config.yml'), 'application/yaml');
    expect(uploadContentTypeFor('talk.srt'), 'text/plain');
  });

  test('a type Jarvis cannot read is not offered', () {
    for (final extension in ['xlsx', 'doc', 'exe', 'zip', 'gif', 'heic']) {
      expect(uploadableFileExtensions, isNot(contains(extension)));
    }
  });
}
