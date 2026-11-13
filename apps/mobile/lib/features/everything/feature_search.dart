import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';

/// Everyday words, in English and Dutch, that should lead to a feature even
/// though they are not its name: "bills" finds Finance, "dagboek" finds Journal.
/// Keyed by tile id. Lower case; accents are ignored when matching.
const featureAliases = <String, List<String>>{
  'activity': ['feed', 'recent', 'activiteit', 'overzicht'],
  'today': ['agenda', 'planner', 'schedule', 'vandaag', 'dagplanning', 'day'],
  'tasks': ['todo', 'to do', 'to-do', 'taken', 'taak', 'jobs', 'errands'],
  'reminders': [
    'alarm',
    'alerts',
    'remind',
    'herinnering',
    'herinneringen',
    'wekker',
  ],
  'lists': [
    'shopping',
    'groceries',
    'packing',
    'checklist',
    'boodschappen',
    'lijstje',
  ],
  'habits': ['routines', 'streak', 'gewoonte', 'gewoontes', 'daily check-in'],
  'journal': ['diary', 'dagboek', 'log', 'write', 'schrijven', 'gratitude'],
  'decisions': [
    'choices',
    'keuzes',
    'besluiten',
    'beslissingen',
    'predictions',
  ],
  'play': [
    'game',
    'bets',
    'quests',
    'ghost',
    'score',
    'level',
    'spel',
    'uitdagingen',
    'punten',
  ],
  'know-me': [
    'train',
    'quiz',
    'memory game',
    'trust',
    'ladder',
    'unknown',
    'fog',
    'ken mij',
    'vertrouwen',
  ],
  'talk-differently': [
    'stranger',
    'rehearse',
    'practise',
    'practice',
    'past self',
    'past me',
    'council',
    'advisers',
    'oefenen',
    'raad',
  ],
  'weekly-review': ['review', 'reflection', 'weekoverzicht', 'terugblik'],
  'briefing': ['morning', 'ochtend', 'summary', 'news', 'ochtendoverzicht'],
  'chats': ['conversations', 'gesprekken', 'messages', 'berichten', 'chat'],
  'voice': ['talk', 'praten', 'spraak', 'microphone', 'call', 'bellen'],
  'whatsapp': ['wa', 'berichten', 'messages'],
  'inbox': [
    'promises',
    'beloftes',
    'commitments',
    'toezeggingen',
    'follow-ups',
    'follow ups',
    'waiting',
  ],
  'approvals': ['permissions', 'toestemming', 'goedkeuren', 'confirm', 'allow'],
  'channels': ['signal', 'telegram', 'sms', 'kanalen'],
  'memory': ['remember', 'facts', 'geheugen', 'onthouden', 'forget'],
  'library': ['notes', 'notities', 'saved', 'bookmarks', 'bibliotheek'],
  'people': [
    'contacts',
    'contacten',
    'friends',
    'vrienden',
    'family',
    'familie',
    'birthdays',
    'verjaardagen',
    'relationships',
  ],
  'timeline': ['history', 'geschiedenis', 'tijdlijn', 'past'],
  'files': [
    'documents',
    'documenten',
    'bestanden',
    'uploads',
    'pdf',
    'photos',
    'foto',
    'fotos',
  ],
  'graph': ['connections', 'network', 'verbindingen', 'map'],
  'projects': ['projecten', 'work', 'werk', 'goals', 'doelen'],
  'finance': [
    'bills',
    'rekeningen',
    'subscriptions',
    'abonnementen',
    'money',
    'geld',
    'budget',
    'expenses',
    'uitgaven',
    'spending',
    'portfolio',
    'invest',
    'beleggen',
    'bank',
  ],
  'missions': ['goals', 'doelen', 'campaign', 'plans'],
  'automations': ['workflows', 'rules', 'regels', 'schedule', 'triggers'],
  'watches': ['monitor', 'price', 'prijs', 'track', 'alert', 'volgen'],
  'modes': ['focus', 'sleep', 'slaap', 'ambient'],
  'coding': ['code', 'programming', 'github', 'developer'],
  'agents': ['helpers', 'assistenten', 'bots'],
  'integrations': [
    'connect',
    'koppelingen',
    'apps',
    'mcp',
    'gmail',
    'calendar',
    'agenda',
  ],
  'skills': ['abilities', 'vaardigheden', 'playbooks'],
  'persona': ['personality', 'persoonlijkheid', 'style', 'tone'],
  'profiles': ['accounts', 'workspaces', 'profielen'],
  'learning': ['lessons', 'lessen', 'improve', 'leren'],
  'devices': ['phone', 'telefoon', 'location', 'locatie', 'places'],
  'usage': ['cost', 'kosten', 'tokens', 'spend', 'gebruik'],
  'notifications': ['alerts', 'meldingen', 'push', 'notificaties'],
  'settings': [
    'preferences',
    'instellingen',
    'account',
    'privacy',
    'language',
    'taal',
    'theme',
    'thema',
    'dark mode',
  ],
};

/// Lower case with the common Latin accents removed, so "Café" matches "cafe".
String foldForSearch(String text) {
  const from = 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿ';
  const to = 'aaaaaaceeeeiiiinooooouuuuyy';
  final buffer = StringBuffer();
  for (final rune in text.toLowerCase().runes) {
    final char = String.fromCharCode(rune);
    final index = from.indexOf(char);
    buffer.write(index < 0 ? char : to[index]);
  }
  return buffer.toString().trim();
}

/// How well [spec] answers [query]: 0 when it does not. An exact feature name
/// beats a name that merely starts with it, which beats an alias, which beats a
/// word in the description.
int featureScore(
  TileSpec spec,
  String query, {
  Map<String, List<String>>? aliases,
}) {
  final needle = foldForSearch(query);
  if (needle.isEmpty) return 1;
  final name = foldForSearch(spec.name);
  var best = 0;
  void consider(int score) {
    if (score > best) best = score;
  }

  if (name == needle) consider(100);
  if (name.startsWith(needle)) consider(90);
  if (name.split(RegExp(r'[\s-]+')).any((word) => word.startsWith(needle))) {
    consider(80);
  }
  if (name.contains(needle)) consider(70);
  for (final alias
      in (aliases ?? featureAliases)[spec.id] ?? const <String>[]) {
    final folded = foldForSearch(alias);
    if (folded == needle) consider(60);
    if (folded.startsWith(needle)) consider(50);
    if (folded.contains(needle)) consider(40);
  }
  if (foldForSearch(spec.description).contains(needle)) consider(30);
  if (foldForSearch(spec.category.label).contains(needle)) consider(20);
  return best;
}

/// The features that match [query], best first; registry order breaks ties.
List<TileSpec> searchFeatures(String query, {List<TileSpec>? from}) {
  final specs = from ?? tileSpecs;
  final scored =
      [
        for (final (index, spec) in specs.indexed)
          (spec: spec, index: index, score: featureScore(spec, query)),
      ].where((entry) => entry.score > 0).toList()..sort((a, b) {
        final byScore = b.score.compareTo(a.score);
        return byScore != 0 ? byScore : a.index.compareTo(b.index);
      });
  return [for (final entry in scored) entry.spec];
}
