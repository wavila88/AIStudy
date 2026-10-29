# Feature Specification: Clear Search Results

**Feature Branch**: `002-clear-search`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Once the search is complete, show a button to clear the search; once the results are cleaned, this button should disappear." (originally written by the user as FR-012)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clear the results after a search (Priority: P1)

After a search finishes and its results are on screen, the user can press a **Limpiar** button to start over. The results disappear, the query box is emptied and ready for a new question, and the button goes away.

**Why this priority**: It is the whole feature; without it the user must delete the text and refresh the page by hand to get a clean screen.

**Independent Test**: Run a search that returns results, confirm **Limpiar** is visible, press it, and confirm the results are gone, the query box is empty and **Limpiar** is no longer visible.

**Acceptance Scenarios**:

1. **Given** the page has just loaded and no search was made, **When** the user looks at the page, **Then** the **Limpiar** button is not shown.
2. **Given** a search has completed and results are displayed, **When** the user looks at the page, **Then** the **Limpiar** button is shown.
3. **Given** results are displayed, **When** the user presses **Limpiar**, **Then** the results and any messages disappear, the query box is empty and ready for typing, and the **Limpiar** button is no longer shown.
4. **Given** the results were cleared, **When** the user types a new query and searches, **Then** the search works exactly as before and the **Limpiar** button appears again with the new results.

---

### User Story 2 - Button only when it makes sense (Priority: P2)

The **Limpiar** button is shown only when there are results to clear. It does not appear while a search is running or after a search that did not produce results.

**Why this priority**: Avoids a confusing button that does nothing, but depends on the main behavior above.

**Independent Test**: Trigger an empty query, an over-limit query and a failed search; verify the button never appears in those cases, nor while the search is in progress.

**Acceptance Scenarios**:

1. **Given** a search is in progress, **When** the user looks at the page, **Then** the **Limpiar** button is not shown.
2. **Given** a search ended in a validation message (empty or too long query), **When** the user looks at the page, **Then** the **Limpiar** button is not shown.
3. **Given** a search ended in a service error message, **When** the user looks at the page, **Then** the **Limpiar** button is not shown.
4. **Given** a search returned no items, **When** the user looks at the page, **Then** the **Limpiar** button is not shown.

---

### Edge Cases

- The user edits the text in the query box after results appear and then presses **Limpiar**: the box is emptied and the results are removed regardless of the edit.
- The user presses **Limpiar** twice quickly: the second press has no effect and no error is shown.
- Clearing never triggers a new search and does not depend on the search service being available.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST show a **Limpiar** button only when a search has completed and its results are displayed.
- **FR-002**: When the user presses **Limpiar**, the system MUST remove the displayed results and any related messages, empty the query box, and leave it ready for typing.
- **FR-003**: After the results are cleared, the system MUST stop showing the **Limpiar** button.
- **FR-004**: The system MUST NOT show the **Limpiar** button on the initial page, while a search is in progress, or after a search that ended in a validation message, a service error or no results.
- **FR-005**: Pressing **Limpiar** MUST NOT start a search and MUST NOT depend on the search service being available.
- **FR-006**: After clearing, the user MUST be able to run a new search with the same behavior as before this feature.

### Key Entities *(include if feature involves data)*

- **Displayed Results**: The set of up to 3 results currently on screen; this feature only decides when they are shown or removed. Nothing is stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can return the page to its initial state with a single action, in under 2 seconds.
- **SC-002**: In 100% of clears, the page ends with no results, no messages, an empty query box and no **Limpiar** button.
- **SC-003**: The **Limpiar** button is never shown in the initial, in-progress, validation-error, service-error or no-results states.
- **SC-004**: 90% of first-time users find and use the **Limpiar** button without instructions.

## Assumptions

- This feature builds on the already released Semantic Search Page (001) and changes only its screen behavior.
- The button label is **Limpiar**, consistent with the Spanish interface.
- Clearing also empties the text the user typed in the query box. This is an assumption that the user can confirm or change.
- The button is shown only after a search that returned results; after errors, validation messages or no results the user can already correct or retype the query directly.
- Clearing is not a stored action: nothing is saved or logged, in line with the existing rule of not keeping user queries.
- Keyboard-only use is expected to work the same as mouse use.
