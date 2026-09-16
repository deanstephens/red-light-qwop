mergeInto(LibraryManager.library, {
  RLQ_IsDocumentHidden: function () {
    return (typeof document !== 'undefined' && document.hidden) ? 1 : 0;
  },
  RLQ_CopyText: function (ptr) {
    var text = UTF8ToString(ptr);
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text);
        return 1;
      }
    } catch (e) {}
    return 0;
  }
});
