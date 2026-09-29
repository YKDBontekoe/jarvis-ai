import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/recent_searches_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('recent searches stay on device and clear on sign-out helper', () async {
    SharedPreferences.setMockInitialValues({});
    final store = await RecentSearchesStore.open();
    await store.remember('quarterly review');
    await store.remember('files');
    expect(store.read(), ['files', 'quarterly review']);
    await RecentSearchesStore.clearAll();
    final afterClear = await RecentSearchesStore.open();
    expect(afterClear.read(), isEmpty);
  });
}
