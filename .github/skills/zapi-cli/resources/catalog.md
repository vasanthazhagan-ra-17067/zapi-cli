# Zoho Cliq API Catalog

Index of all OpenAPI specs in the [`resources/`]() folder, grouped by domain.

---

## Contacts & Buddies

| File | Description |
|------|-------------|
| [accept-invite.yaml](accept-invite.yaml) | Accept a pending contact invite from another user |
| [add-to-contact-presence-key.yaml](add-to-contact-presence-key.yaml) | Subscribe a set of user ZUIDs to the contacts presence topic |
| [decline-invite.yaml](decline-invite.yaml) | Decline (remind me later) a pending contact invite from another user |
| [delete-buddy.yaml](delete-buddy.yaml) | Remove a user from the authenticated user's contacts/buddy list |
| [get-buddies-no-type.yaml](get-buddies-no-type.yaml) | Retrieve the full buddy/contact list for the authenticated user |
| [get-buddies-with-type.yaml](get-buddies-with-type.yaml) | Retrieve buddies filtered by invitation or friendship status |
| [get-frequent-users.yaml](get-frequent-users.yaml) | Retrieve the authenticated user's buddy list sorted by interaction frequency |
| [get-org-contacts.yaml](get-org-contacts.yaml) | Search for users within the organisation by a search string |
| [get-presence-keys.yaml](get-presence-keys.yaml) | Retrieve the list of presence subscription keys available to the authenticated user |
| [get-presence.yaml](get-presence.yaml) | Retrieve presence status for one or more users |
| [get-profile-image.yaml](get-profile-image.yaml) | Download the thumbnail profile image for a message source (bot, channel, command, applet, or extension) |
| [get-user-info.yaml](get-user-info.yaml) | Retrieve profile and presence information for a single user by ZUID |
| [get-users-email.yaml](get-users-email.yaml) | Retrieve email addresses for a batch of users identified by their ZUIDs |
| [invite-contact-email.yaml](invite-contact-email.yaml) | Send a contact invite to a user identified by their email address |
| [invite-contact-zuid.yaml](invite-contact-zuid.yaml) | Send a contact invite to a user identified by their ZUID |
| [sync-contacts.yaml](sync-contacts.yaml) | Sync the local contact database with the server using checksum-based diffing |

---

## Channels

| File | Description |
|------|-------------|
| [add-members-to-channels.yaml](add-members-to-channels.yaml) | Invite one or more users to a channel by posting a comma-separated list of user ZUIDs |
| [change-channel-participant-role.yaml](change-channel-participant-role.yaml) | Change the role of a participant within a channel |
| [clear-all-channel-messages.yaml](clear-all-channel-messages.yaml) | Clear all messages in the specified channel |
| [create-channel.yaml](create-channel.yaml) | Create a new channel with the specified name, scope, reply mode, and optional members/permissions |
| [delete-channel.yaml](delete-channel.yaml) | Permanently delete the channel identified by its organisation channel ID (ocid) |
| [edit-channel-info.yaml](edit-channel-info.yaml) | Update an existing channel's name, description, visibility, team membership, reply configuration, and optional photo |
| [edit-channel-permissions.yaml](edit-channel-permissions.yaml) | Update the permission for a specific action and role within a channel |
| [fetch-team-channels.yaml](fetch-team-channels.yaml) | Retrieve channels accessible to the authenticated user, optionally filtered by name or team IDs |
| [get-channel-categories.yaml](get-channel-categories.yaml) | Retrieve the list of channel categories available to the authenticated user |
| [get-channel-profile-image-from-stratus.yaml](get-channel-profile-image-from-stratus.yaml) | Download a channel profile image stored in Stratus using the channel scope ID and photo ID |
| [get-channel-with-name.yaml](get-channel-with-name.yaml) | Retrieve a Cliq channel by its unfurl (unique) name |
| [get-channel-with.yaml](get-channel-with.yaml) | Retrieve the details of a single Cliq channel identified by its OCID |
| [get-channels-with.yaml](get-channels-with.yaml) | Retrieve a list of channels matching the specified channel IDs, optionally filtered by channel mode |
| [get-channels.yaml](get-channels.yaml) | Retrieve a paginated list of channels available to the authenticated user, optionally filtered by category, scope, or team |
| [join-channel.yaml](join-channel.yaml) | Join the channel identified by the given channel OCID on behalf of the specified user |
| [leave-from-channel.yaml](leave-from-channel.yaml) | Remove the authenticated user from the specified channel |
| [remove-members-from-channel.yaml](remove-members-from-channel.yaml) | Remove one or more members from a channel identified by its organisation channel ID |
| [search-channels.yaml](search-channels.yaml) | Search for channels the authenticated user is allowed to join or has already joined |
| [set-title-channel-ocid.yaml](set-title-channel-ocid.yaml) | Update the display name of a channel identified by its OCID |
| [sync-channels.yaml](sync-channels.yaml) | Synchronise the authenticated user's channel list from the server using incremental timestamp-based sync |
| [update-auto-follow-thread-of-channel.yaml](update-auto-follow-thread-of-channel.yaml) | Enable or disable automatic thread following for the authenticated user in a specific channel |

---

## Chats & Messages

| File | Description |
|------|-------------|
| [add-members.yaml](add-members.yaml) | Add one or more members to an existing Cliq channel |
| [add-sticky-message.yaml](add-sticky-message.yaml) | Pin (sticky) a message in a chat by setting its expiry time and optional notification flag |
| [create-chat.yaml](create-chat.yaml) | Create a new individual or group instant-message chat for the given list of users |
| [delete-message.yaml](delete-message.yaml) | Delete a message |
| [edit-attachment-with-uid.yaml](edit-attachment-with-uid.yaml) | Edit an existing attachment message in a chat by updating its comment text and notification preference |
| [edit-message-with-uid.yaml](edit-message-with-uid.yaml) | Edit an existing channel message identified by its UID, replacing its text content |
| [fork-chat-with-chid.yaml](fork-chat-with-chid.yaml) | Fork an existing chat into a new chat, optionally copying participants and seeding it from a specific message |
| [forward-attachment.yaml](forward-attachment.yaml) | Forward an existing attachment to a target chat or channel, optionally with a comment |
| [forward-messages.yaml](forward-messages.yaml) | Forward one or more messages from a source chat to one or more target chats or users |
| [get-attachment.yaml](get-attachment.yaml) | Download an attachment file (or its thumbnail) identified by its remote URL path |
| [get-chat-and-guest-info.yaml](get-chat-and-guest-info.yaml) | Retrieve guest metadata and the associated chat for an external guest session using a pre-issued guest URL |
| [get-chat.yaml](get-chat.yaml) | Retrieve a single chat by its chat ID for the specified user |
| [get-chats-chids.yaml](get-chats-chids.yaml) | Retrieve one or more chats by their chat IDs (chids) |
| [get-chats-from-to-limit.yaml](get-chats-from-to-limit.yaml) | Retrieve a paginated list of chats for the authenticated user, optionally filtered by time range or threaded view |
| [get-chats-recipient-search-string.yaml](get-chats-recipient-search-string.yaml) | Retrieve a paginated list of chats filtered by recipient and an optional title search string |
| [get-chats-search-string-search-option.yaml](get-chats-search-string-search-option.yaml) | Search chats and/or threads by title up to a given time, optionally filtered to threads only |
| [get-edit-history.yaml](get-edit-history.yaml) | Retrieve the full edit history transcript for a specific message in a chat |
| [get-media.yaml](get-media.yaml) | Retrieve a paginated list of media items (images, videos, audio, files, or links) shared in a specific chat |
| [get-members-of-groups.yaml](get-members-of-groups.yaml) | Retrieve the members belonging to one or more groups identified by their group IDs |
| [get-mentions-list.yaml](get-mentions-list.yaml) | Retrieve a paginated list of messages in which the authenticated user has been mentioned |
| [get-message.yaml](get-message.yaml) | Retrieve a single message by its UID from the specified chat |
| [get-muted-chats.yaml](get-muted-chats.yaml) | Retrieve the list of chats that the user has muted, up to a specified limit |
| [get-participants.yaml](get-participants.yaml) | Retrieve a paginated list of participants for a given chat, with optional filtering |
| [get-starred-messages.yaml](get-starred-messages.yaml) | Retrieve a paginated list of messages starred by the authenticated user |
| [get-sticky-messages.yaml](get-sticky-messages.yaml) | Retrieve the sticky message pinned to the specified chat |
| [get-transcripts.yaml](get-transcripts.yaml) | Retrieve the message transcript for a given chat, with optional time-range and pagination controls |
| [get-unfurled-data.yaml](get-unfurled-data.yaml) | Fetch unfurled link-preview metadata for a given URL |
| [get-user-read-status.yaml](get-user-read-status.yaml) | Retrieve the per-user read status for a specific message in a chat |
| [join-chat-through-http.yaml](join-chat-through-http.yaml) | Attach the current user to an existing chat session identified by a chat ID and optional session ID |
| [leave-group-chat.yaml](leave-group-chat.yaml) | Remove the authenticated user from the specified group chat |
| [make-chat-with-chid.yaml](make-chat-with-chid.yaml) | Pin or unpin a chat identified by its chid, optionally assigning it to a pinned category |
| [mark-chids-as-read-batch.yaml](mark-chids-as-read-batch.yaml) | Mark one or more chats as read for the authenticated user in a single batch request (up to 100 chids) |
| [mark-chids-as-read-upto-msg-uid.yaml](mark-chids-as-read-upto-msg-uid.yaml) | Mark all messages in a chat as read up to and including the specified message UID |
| [mark-message-as-unread.yaml](mark-message-as-unread.yaml) | Mark a specific chat message as unread for the given user |
| [mark-reaction-msg-uid-as-read.yaml](mark-reaction-msg-uid-as-read.yaml) | Mark the specified reaction message as read for the given chat channel |
| [mute-chat.yaml](mute-chat.yaml) | Mute a chat for the authenticated user for a specified duration |
| [post-temp-message.yaml](post-temp-message.yaml) | Post a temporary integration-response message to a chat identified by its channel ID |
| [quit-chat.yaml](quit-chat.yaml) | Remove the authenticated user from a chat session identified by the given chat ID and session ID |
| [remove-members.yaml](remove-members.yaml) | Remove one or more members from a Cliq channel |
| [remove-sticky-message.yaml](remove-sticky-message.yaml) | Remove the sticky message from the specified chat |
| [send-attachment-public.yaml](send-attachment-public.yaml) | Upload a file attachment and deliver it as a message to the specified chat |
| [send-giphy-message.yaml](send-giphy-message.yaml) | Send a Giphy GIF as a message to a chat channel |
| [send-text-message-to-chid.yaml](send-text-message-to-chid.yaml) | Send a plain-text message to a Cliq chat channel identified by chid |
| [send-text-message-to-zuid.yaml](send-text-message-to-zuid.yaml) | Send a plain-text direct message to a user identified by their ZUID |
| [set-history.yaml](set-history.yaml) | Enable or disable message history for a chat identified by its channel ID |
| [set-title-chid.yaml](set-title-chid.yaml) | Set the display title of a chat identified by its chid |
| [star-message.yaml](star-message.yaml) | Star a message in a channel by recording a star type against the message identifier |
| [unstar-message.yaml](unstar-message.yaml) | Remove the star from a previously starred message |

---

## Threads

| File | Description |
|------|-------------|
| [close-thread.yaml](close-thread.yaml) | Close a thread |
| [follow-thread.yaml](follow-thread.yaml) | Follow a thread so the current user receives notifications for it |
| [get-thread-head-message.yaml](get-thread-head-message.yaml) | Retrieve the head (main) message of the specified thread |
| [get-thread-non-followers.yaml](get-thread-non-followers.yaml) | Retrieve the list of users who are not following a given thread, with optional filtering |
| [get-thread.yaml](get-thread.yaml) | Retrieve the thread (chat) details for the given channel/chat ID |
| [get-threads.yaml](get-threads.yaml) | Retrieve a paginated list of threads belonging to a channel, with optional filtering |
| [modify-thread-followers.yaml](modify-thread-followers.yaml) | Add one or more users as followers of a thread |
| [reopen-thread.yaml](reopen-thread.yaml) | Reopen a closed thread |
| [unfollow-thread.yaml](unfollow-thread.yaml) | Unfollow a thread so the current user no longer receives notifications for it |

---

## Reactions

| File | Description |
|------|-------------|
| [add-reaction.yaml](add-reaction.yaml) | Add an emoji reaction to a chat message |
| [get-reaction-info.yaml](get-reaction-info.yaml) | Retrieve the full reaction details for a specific message in a chat |
| [remove-reaction.yaml](remove-reaction.yaml) | Remove an emoji reaction from a chat message |

---

## Reminders

| File | Description |
|------|-------------|
| [add-reminder-user.yaml](add-reminder-user.yaml) | Add one or more users to an existing reminder |
| [add-reminder.yaml](add-reminder.yaml) | Create a new reminder, optionally linked to a chat message and/or targeted at another user/entity |
| [clear-completed-reminders.yaml](clear-completed-reminders.yaml) | Delete all completed reminders for the current user or for others |
| [complete-reminder.yaml](complete-reminder.yaml) | Mark a reminder as complete (or revert back to incomplete) |
| [delete-reminder-user.yaml](delete-reminder-user.yaml) | Remove a user from an existing reminder |
| [delete-reminder.yaml](delete-reminder.yaml) | Permanently delete a reminder by its identifier |
| [edit-reminder.yaml](edit-reminder.yaml) | Update the content and/or trigger time of an existing reminder |
| [get-reminders.yaml](get-reminders.yaml) | Fetch a paginated list of reminders filtered by category and optional date range |
| [remind-reminder-user.yaml](remind-reminder-user.yaml) | Trigger a reminder notification for a specific user on a reminder |

---

## Scheduled Messages

| File | Description |
|------|-------------|
| [delete-all-scheduled-message.yaml](delete-all-scheduled-message.yaml) | Delete all scheduled messages in a chat |
| [delete-scheduled-message.yaml](delete-scheduled-message.yaml) | Delete a single scheduled message from a chat |
| [edit-schedule-message.yaml](edit-schedule-message.yaml) | Edit the text (and optionally the time) of an existing scheduled message |
| [get-all-scheduled-messages.yaml](get-all-scheduled-messages.yaml) | Retrieve all scheduled messages for a specific chat |
| [get-scheduled-attachment.yaml](get-scheduled-attachment.yaml) | Download an attachment associated with a scheduled message |
| [re-schedule-message.yaml](re-schedule-message.yaml) | Reschedule a single scheduled message to a new time or status |
| [reschedule-all-messages.yaml](reschedule-all-messages.yaml) | Bulk-reschedule all scheduled messages in a chat from an old time/status to a new one |
| [schedule-attachment.yaml](schedule-attachment.yaml) | Upload a file attachment as a scheduled message in a chat |
| [schedule-giphy-message.yaml](schedule-giphy-message.yaml) | Schedule a Giphy GIF message to be sent in a chat at a future time |
| [schedule-text-message.yaml](schedule-text-message.yaml) | Schedule a text message to be sent in a chat at a future time |
| [send-all-scheduled-message.yaml](send-all-scheduled-message.yaml) | Immediately send all scheduled messages in a chat |
| [send-scheduled-message.yaml](send-scheduled-message.yaml) | Immediately send a single scheduled message |

---

## Calls & Meetings

| File | Description |
|------|-------------|
| [clear-missed-call-count.yaml](clear-missed-call-count.yaml) | Clear the missed 1-2-1 call count for the authenticated user |
| [clear-unseen-ongoing-meetings.yaml](clear-unseen-ongoing-meetings.yaml) | Mark all unseen ongoing meetings as seen for the authenticated user |
| [get-call-history.yaml](get-call-history.yaml) | Retrieve the call history for the authenticated user |
| [get-group-call-user-info.yaml](get-group-call-user-info.yaml) | Retrieve user information used for group call context |
| [get-missed-call-count.yaml](get-missed-call-count.yaml) | Retrieve the count of missed 1-2-1 calls and the last-viewed timestamp for the authenticated user |
| [get-ongoing-meetings.yaml](get-ongoing-meetings.yaml) | Retrieve the list of live (ongoing) meetings for the authenticated user |
| [group-call-initiation-info.yaml](group-call-initiation-info.yaml) | Retrieve initiation and join information for a group/conference call by its call ID |

---

## Pinned Chats & Folders

| File | Description |
|------|-------------|
| [expand-or-collapse-pinned-folder-with.yaml](expand-or-collapse-pinned-folder-with.yaml) | Expand or collapse a pinned folder (pin category) identified by its ID |
| [get-all-pinned-categories.yaml](get-all-pinned-categories.yaml) | Retrieve all pinned categories (pin folders) for the authenticated user, optionally including chat metadata |
| [reorder-pinned-chat.yaml](reorder-pinned-chat.yaml) | Reorder a pinned chat within a pin category, optionally placing it above a specified chat |
| [reorder-pinned-folder.yaml](reorder-pinned-folder.yaml) | Update the position of a pinned folder by specifying which folder it should appear above |

---

## Bots, Commands & Forms

| File | Description |
|------|-------------|
| [declare-form.yaml](declare-form.yaml) | Submit a completed interactive form along with all selected field values |
| [get-command-image.yaml](get-command-image.yaml) | Download the photo associated with a bot command by its photo ID |
| [get-slash-commands-suggestions.yaml](get-slash-commands-suggestions.yaml) | Fetch dynamic input suggestions for a slash command based on the current chat context |
| [get-slash-commands.yaml](get-slash-commands.yaml) | Retrieve the list of available slash commands with incremental-sync support |
| [get-user-bots.yaml](get-user-bots.yaml) | Retrieve the list of bots for the authenticated user |
| [grant-consent.yaml](grant-consent.yaml) | Grant (or deny) consent for a slash-command execution |
| [invoke-dre-function.yaml](invoke-dre-function.yaml) | Invoke a DRE (Dynamic Response Engine) function by name, triggering bot button/action handlers for a given channel message |
| [invoke-system-action.yaml](invoke-system-action.yaml) | Invoke a named system action for a given channel, optionally scoped to a session |
| [modify-form-input.yaml](modify-form-input.yaml) | Fetch dynamic input suggestions/options for a form field as the user types a search query |
| [modify-form.yaml](modify-form.yaml) | Notify the server of a field-level change in an interactive form, returning updated form state |
| [subscribe-bot.yaml](subscribe-bot.yaml) | Subscribe the authenticated user to a bot |
| [sync-event-message.yaml](sync-event-message.yaml) | Sync the event card for a specific message in a chat channel |
| [un-subscribe-bot.yaml](un-subscribe-bot.yaml) | Unsubscribe the authenticated user from a bot |
| [update-event-card-rsvp.yaml](update-event-card-rsvp.yaml) | Update the RSVP status of the authenticated user for a calendar event card |
| [upload-attachments.yaml](upload-attachments.yaml) | Upload a file attachment for a specific field in an interactive form |

---

## User Status & Presence

| File | Description |
|------|-------------|
| [clear-all-custom-user-status.yaml](clear-all-custom-user-status.yaml) | Clear all custom statuses set for the specified user, resetting them to default |
| [get-current-status.yaml](get-current-status.yaml) | Retrieve the current effective status (ephemeral or persistent) for the authenticated user |
| [get-custom-statuses.yaml](get-custom-statuses.yaml) | Retrieve the list of custom statuses configured by the authenticated user |
| [get-user-statuses.yaml](get-user-statuses.yaml) | Retrieve the list of user status modules available for the authenticated user |
| [remove-ephereal-status.yaml](remove-ephereal-status.yaml) | Remove the currently active ephemeral status for the authenticated user, reverting to the underlying persistent status |
| [set-ephemeral-status.yaml](set-ephemeral-status.yaml) | Set a temporary (ephemeral) presence status for the authenticated user, optionally with an expiry time |
| [set-typing-status.yaml](set-typing-status.yaml) | Publish a typing or idle status info-message to a chat |
| [set-user-status.yaml](set-user-status.yaml) | Set the presence status and optional status message for the specified user |

---

## Teams, Departments & Organisation

| File | Description |
|------|-------------|
| [get-department-mems-and-meetings.yaml](get-department-mems-and-meetings.yaml) | Retrieve the members of a specific department along with their active calls, check-in status, and presence |
| [get-department.yaml](get-department.yaml) | Retrieve the details of a specific department within an organisation |
| [get-departments.yaml](get-departments.yaml) | Retrieve a paginated, optionally filtered list of departments for the authenticated user |
| [get-organisation.yaml](get-organisation.yaml) | Retrieve the organisation details for the authenticated user |
| [get-teams.yaml](get-teams.yaml) | Retrieve the list of teams available to the authenticated user, optionally filtered to joined teams |
| [get-wms-domain-info.yaml](get-wms-domain-info.yaml) | Retrieve the WMS domain and subdomain information for the authenticated user |

---

## Attendance

| File | Description |
|------|-------------|
| [get-user-check-in-out-status.yaml](get-user-check-in-out-status.yaml) | Retrieve the check-in/check-out and presence status for a specific user |
| [get-user-remote-data.yaml](get-user-remote-data.yaml) | Fetch remote user data (attendance status, department, recently-accessed departments, remote-work tools) |
| [perform-check-in-or-check-out.yaml](perform-check-in-or-check-out.yaml) | Check in or check out the current user |

---

## Search

| File | Description |
|------|-------------|
| [search-messages.yaml](search-messages.yaml) | Search messages across chats using filter criteria such as text content, sender, date range, or file type |
| [search-users.yaml](search-users.yaml) | Search for users by name or identifier with an optional result limit |

---

## Settings & Preferences

| File | Description |
|------|-------------|
| [get-hidden-chat-products.yaml](get-hidden-chat-products.yaml) | Retrieve the set of product identifiers whose chatlet integrations are hidden in the chat UI |
| [get-non-state-supported-plan-details.yaml](get-non-state-supported-plan-details.yaml) | Retrieve plan details for features not managed by the server-side state-sync mechanism |
| [get-state-supported-plan-details.yaml](get-state-supported-plan-details.yaml) | Retrieve client-sync state-supported plan details (feature flags, configurations, and settings) for the authenticated user |
| [set-emoji-skin-tone-settings.yaml](set-emoji-skin-tone-settings.yaml) | Update the authenticated user's emoji skin-tone preference |
| [set-read-receipt-settings.yaml](set-read-receipt-settings.yaml) | Enable or disable read-receipt delivery for the authenticated user |

---

## Miscellaneous

| File | Description |
|------|-------------|
| [get-country-codes.yaml](get-country-codes.yaml) | Retrieve the list of country codes, optionally filtered by version |
| [get-gifs.yaml](get-gifs.yaml) | Search for GIFs by keyword with optional pagination |
| [get-location-address.yaml](get-location-address.yaml) | Retrieve the address for a specific geographic coordinate |
| [get-location-suggestions.yaml](get-location-suggestions.yaml) | Search for location suggestions matching a text query, with optional geographic boundary |
