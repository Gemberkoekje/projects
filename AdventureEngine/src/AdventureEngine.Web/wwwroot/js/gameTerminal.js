window.adventureTerminal = window.adventureTerminal || {};
window.adventureTerminal.scrollToBottom = (element) => {
    if (!element) {
        return;
    }

    element.scrollTop = element.scrollHeight;
};
