import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/ui/plain_text.dart';

void main() {
  test('markdown previews drop syntax but keep the words', () {
    expect(
      plainPreview('## Result\n\n**14 emails** triaged, see [the docs](https://x.y).\n- next: `retry`'),
      'Result · 14 emails triaged, see the docs. · next: retry',
    );
  });

  test('previews are shortened with an ellipsis', () {
    final text = plainPreview('word ' * 100, maxLength: 20);
    expect(text.length, lessThanOrEqualTo(20));
    expect(text, endsWith('…'));
    expect(plainPreview(''), '');
  });

  test('coding tasks are titled by their Change line', () {
    expect(
      codingTaskTitle('Jarvis is improving its own code.\nChange: Skip broken servers\n\nProblem: x'),
      'Skip broken servers',
    );
    expect(codingTaskTitle('Fix the greeting\nmore detail'), 'Fix the greeting');
    expect(codingTaskTitle('  \n '), 'Coding task');
    expect(codingTaskTitle('x' * 200, maxLength: 10).length, 10);
  });
}
