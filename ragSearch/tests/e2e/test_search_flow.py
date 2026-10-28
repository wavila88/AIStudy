from playwright.sync_api import expect

QUERY = "me rechazan la tarjeta aunque tengo saldo"
EXPECTED_DOC = "KB-01"

# Runs in every document before its own scripts. When the page starts navigating away
# (the search was submitted), it records whether "Buscando…" was visible and the button disabled.
RECORD_LOADING_STATE = """
window.addEventListener("beforeunload", () => {
  const loading = document.querySelector("[data-testid='loading']");
  const button = document.querySelector("[data-testid='search-button']");
  const ok = !!loading && !loading.hidden && !!button && button.disabled;
  sessionStorage.setItem("loadingAtSubmit", String(ok));
});
"""


def test_search_returns_three_ranked_results(page, app_url):
    page.add_init_script(RECORD_LOADING_STATE)
    page.goto(app_url)
    expect(page.get_by_test_id("loading")).to_be_hidden()

    page.get_by_test_id("query-input").fill(QUERY)
    page.get_by_test_id("search-button").click()

    results = page.get_by_test_id("results")
    expect(results).to_be_visible(timeout=30_000)

    # US3: while the search ran the user saw "Buscando…" and could not submit again.
    assert page.evaluate("sessionStorage.getItem('loadingAtSubmit')") == "true"
    expect(page.get_by_test_id("loading")).to_be_hidden()

    items = page.get_by_test_id("result-item")
    expect(items).to_have_count(3)

    docs = []
    scores = []
    for i in range(3):
        item = items.nth(i)
        expect(item.get_by_test_id("result-text")).not_to_be_empty()
        expect(item.get_by_test_id("result-doc")).not_to_be_empty()
        expect(item.get_by_test_id("result-score")).not_to_be_empty()
        docs.append(item.get_by_test_id("result-doc").inner_text())
        scores.append(float(item.get_by_test_id("result-score").inner_text().replace(",", ".")))

    assert scores == sorted(scores, reverse=True), f"scores not in descending order: {scores}"
    assert any(EXPECTED_DOC in d for d in docs), f"{EXPECTED_DOC} not among {docs}"
