const mcpSetupPrompt =
    'Help me connect an app or manage the ones I have, in this chat. Show the setup card.';

const mcpCalendarPrompt =
    'Connect my calendar in this chat. Ask me for the calendar link if you need it.';

/// Asks Jarvis to find an app for [what], or write one when none fits.
String mcpBuildPrompt(String what) =>
    'I want Jarvis to work with ${what.trim()}. Check the app directory first. '
    'If nothing fits, build the app yourself, test it, and show me before switching it on.';

const mcpBuildAnyPrompt =
    'Build me a new app so you can work with a service I use. Ask me which service and what you should be able to do with it.';
