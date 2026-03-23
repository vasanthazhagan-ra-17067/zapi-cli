# OpenAPI Spec Generation Progress

Each entry is a public HTTP method in `ZCNetworkService.swift` that makes an outbound HTTP request.

| # | Done | Method | Lines |
|---|------|--------|-------|
| 1 | [x] | `getImage` | 32–55 |
| 2 | [x] | `getOrganisation` | 286–328 |
| 3 | [x] | `getWmsDomainInfo` | 329–363 |
| 4 | [x] | `getStateSupportedPlanDetails` | 364–408 |
| 5 | [x] | `getNonStateSupportedPlanDetails` | 409–444 |
| 6 | [x] | `setEmojiSkinToneSettings` | 445–466 |
| 7 | [x] | `setReadReceiptSettings` | 467–488 |
| 8 | [x] | `getHiddenChatProducts` | 489–532 |
| 9 | [x] | `joinChatThroughHttp` | 533–575 |
| 10 | [x] | `quitChat` | 576–600 |
| 11 | [x] | `createChat` | 601–659 |
| 12 | [x] | `getChats` (from/to/limit) | 660–705 |
| 13 | [x] | `getChats` (chids) | 706–735 |
| 14 | [x] | `getChats` (recipient/searchString) | 736–774 |
| 15 | [x] | `getChat` | 775–806 |
| 16 | [x] | `getThread` | 807–832 |
| 17 | [x] | `getChatAndGuestInfo` | 833–857 |
| 18 | [x] | `getMutedChats` | 858–888 |
| 19 | [x] | `getChats` (searchString/searchOption) | 889–925 |
| 20 | [x] | `getThreads` | 926–993 |
| 21 | [x] | `addMembers` | 994–1019 |
| 22 | [x] | `removeMembers` | 1020–1045 |
| 23 | [x] | `markChidsAsRead` (batch) | 1046–1068 |
| 24 | [x] | `markChidsAsRead` (uptoMsgUid) | 1097–1127 |
| 25 | [x] | `markReactionMsgUidAsRead` | 1128–1159 |
| 26 | [x] | `setTitle` (channel ocid) | 1160–1202 |
| 27 | [x] | `setTitle` (chid) | 1203–1262 |
| 28 | [x] | `muteChat` | 1263–1312 |
| 29 | [x] | `setHistory` | 1313–1347 |
| 30 | [x] | `leaveGroupChat` | 1348–1369 |
| 31 | [x] | `makeChatWithChid` | 1370–1406 |
| 32 | [x] | `reorderPinnedFolder` | 1407–1440 |
| 33 | [x] | `reorderPinnedChat` | 1441–1479 |
| 34 | [x] | `getMedia` | 1480–1541 |
| 35 | [x] | `getDepartments` | 1542–1581 |
| 36 | [x] | `getDepartment` | 1582–1619 |
| 37 | [x] | `getDepartmentMemsAndMeetings` | 1620–1675 |
| 38 | [x] | `getAllPinnedCategories` | 1676–1711 |
| 39 | [x] | `expandOrCollapsePinnedFolderWith` | 1712–1739 |
| 40 | [x] | `forkChatWithChid` | 1740–1791 |
| 41 | [x] | `getChannelCategories` | 1792–1817 |
| 42 | [x] | `syncChannels` | 1818–1862 |
| 43 | [x] | `fetchTeamChannels` | 1863–1898 |
| 44 | [x] | `searchChannels` | 1899–1953 |
| 45 | [x] | `getChannels` | 1954–2023 |
| 46 | [x] | `getChannelsWith` | 2024–2057 |
| 47 | [x] | `getChannelWith` | 2058–2090 |
| 48 | [x] | `getChannelWithName` | 2091–2146 |
| 49 | [x] | `getParticipants` | 2147–2248 |
| 50 | [x] | `getThreadNonFollowers` | 2249–2325 |
| 51 | [x] | `getMembersOfGroups` | 2326–2360 |
| 52 | [x] | `modifyThreadFollowers` | 2361–2406 |
| 53 | [x] | `addMembersToChannels` | 2407–2436 |
| 54 | [x] | `removeMembersFromChannel` | 2437–2467 |
| 55 | [x] | `leaveFromChannel` | 2468–2504 |
| 56 | [x] | `deleteChannel` | 2505–2524 |
| 57 | [x] | `updateAutoFollowThreadOfChannel` | 2525–2550 |
| 58 | [x] | `joinChannel` | 2551–2580 |
| 59 | [x] | `createChannel` | 2581–2679 |
| 60 | [x] | `editChannelInfo` | 2680–2785 |
| 61 | [x] | `editChannelPermissions` | 2786–2808 |
| 62 | [x] | `changeChannelParticipantRole` | 2809–2842 |
| 63 | [x] | `getTeams` | 2843–2874 |
| 64 | [x] | `getTranscripts` | 2875–2945 |
| 65 | [x] | `getCommandImage` | 2946–2954 |
| 66 | [x] | `getAttachment` | 2955–2978 |
| 67 | [x] | `getThreadHeadMessage` | 2979–2996 |
| 68 | [x] | `getMessage` | 2997–3032 |
| 69 | [x] | `sendTextMessage` (to zuid) | 3033–3074 |
| 70 | [x] | `sendTextMessage` (to chid) | 3075–3213 |
| 71 | [x] | `sendGiphyMessage` | 3214–3268 |
| 72 | [x] | `sendAttachment` (public) | 3269–3282 |
| 73 | [x] | `forwardAttachment` | 3590–3654 |
| 74 | [x] | `forwardMessages` | 3655–3715 |
| 75 | [x] | `markMessageAsUnread` | 3716–3750 |
| 76 | [x] | `getStarredMessages` | 3751–3785 |
| 77 | [x] | `starMessage` | 3786–3808 |
| 78 | [x] | `unstarMessage` | 3809–3825 |
| 79 | [x] | `getMentionsList` | 3826–3862 |
| 80 | [x] | `searchMessages` | 3863–3940 |
| 81 | [x] | `getStickyMessages` | 3941–3962 |
| 82 | [x] | `removeStickyMessage` | 3963–3976 |
| 83 | [x] | `addStickyMessage` | 3977–4002 |
| 84 | [x] | `getUnfurledData` | 4003–4032 |
| 85 | [x] | `clearAllChannelMessages` | 4033–4060 |
| 86 | [x] | `invokeDreFunction` | 4061–4132 |
| 87 | [x] | `invokeSystemAction` | 4133–4223 |
| 88 | [x] | `postTempMessage` | 4224–4266 |
| 89 | [x] | `deleteMessage` | 4267–4306 |
| 90 | [x] | `editMessageWithUid` | 4307–4349 |
| 91 | [x] | `editAttachmentWithUid` | 4350–4387 |
| 92 | [x] | `getEditHistory` | 4388–4445 |
| 93 | [x] | `addReaction` | 4446–4449 |
| 94 | [x] | `removeReaction` | 4450–4453 |
| 95 | [x] | `getReactionInfo` | 4454–4502 |
| 96 | [x] | `getUserReadStatus` | 4503–4537 |
| 97 | [x] | `unfollowThread` | 4538–4557 |
| 98 | [x] | `followThread` | 4558–4577 |
| 99 | [x] | `updateEventCardRSVP` | 4578–4621 |
| 100 | [x] | `syncEventMessage` | 4622–4650 |
| 101 | [x] | `closeThread` | 4651–4654 |
| 102 | [x] | `reopenThread` | 4655–4663 |
| 103 | [x] | `addReminder` | 4664–4720 |
| 104 | [x] | `editReminder` | 4721–4762 |
| 105 | [x] | `deleteReminder` | 4763–4784 |
| 106 | [x] | `completeReminder` | 4785–4807 |
| 107 | [x] | `addReminderUser` | 4808–4841 |
| 108 | [x] | `deleteReminderUser` | 4842–4868 |
| 109 | [x] | `remindReminderUser` | 4869–4901 |
| 110 | [x] | `getReminders` | 4902–4950 |
| 111 | [x] | `clearCompletedReminders` | 4951–4976 |
| 112 | [x] | `clearAllCustomUserStatus` | 4977–5005 |
| 113 | [x] | `getUserStatuses` | 5006–5040 |
| 114 | [x] | `setUserStatus` | 5041–5081 |
| 115 | [x] | `setEphemeralStatus` | 5082–5130 |
| 116 | [x] | `removeEpheremalStatus` | 5131–5156 |
| 117 | [x] | `getCurrentStatus` | 5157–5189 |
| 118 | [x] | `getCustomStatuses` | 5190–5222 |
| 119 | [x] | `getPresence` | 5223–5258 |
| 120 | [x] | `setTypingStatus` | 5259–5303 |
| 121 | [x] | `getGroupCallUserInfo` | 5304–5326 |
| 122 | [x] | `getBuddies` (no type) | 5327–5354 |
| 123 | [x] | `getBuddies` (with type) | 5355–5388 |
| 124 | [x] | `syncContacts` | 5389–5412 |
| 125 | [x] | `getPresenceKeys` | 5413–5443 |
| 126 | [x] | `addToContactPresenceKey` | 5444–5476 |
| 127 | [x] | `getOrgContacts` | 5477–5504 |
| 128 | [x] | `deleteBuddy` | 5505–5524 |
| 129 | [x] | `inviteContact` (email) | 5525–5528 |
| 130 | [x] | `inviteContact` (zuid) | 5529–5532 |
| 131 | [x] | `acceptInvite` | 5533–5536 |
| 132 | [x] | `declineInvite` | 5537–5540 |
| 133 | [x] | `getUserInfo` | 5541–5551 |
| 134 | [x] | `searchUsers` | 5552–5555 |
| 135 | [x] | `getFrequentUsers` | 5556–5587 |
| 136 | [x] | `getUsersEmail` | 5588–5594 |
| 137 | [x] | `subscribeBot` | 5627–5659 |
| 138 | [x] | `unSubscribeBot` | 5660–5707 |
| 139 | [x] | `getUserBots` | 5708–5743 |
| 140 | [x] | `grantConsent` | 5744–5816 |
| 141 | [x] | `getCallHistory` | 5817–5887 |
| 142 | [x] | `getOngoingMeetings` | 5888–5921 |
| 143 | [x] | `clearUnseenOngoingMeetings` | 5922–5940 |
| 144 | [x] | `getMissedCallCount` | 5941–5971 |
| 145 | [x] | `groupCallInitiationInfo` | 5972–5990 |
| 146 | [x] | `clearMissedCallCount` | 5991–6019 |
| 147 | [x] | `modifyForm` | 6052–6088 |
| 148 | [x] | `modifyFormInput` | 6089–6126 |
| 149 | [x] | `uploadAttachments` | 6127–6182 |
| 150 | [x] | `declareForm` | 6183–6233 |
| 151 | [x] | `getCountryCodes` | 6234–6265 |
| 152 | [x] | `getLocationAddress` | 6266–6292 |
| 153 | [x] | `getLocationSuggestions` | 6293–6339 |
| 154 | [x] | `getProfileImage` | 6340–6398 |
| 155 | [x] | `getChannelProfileImageFromStratus` | 6399–6455 |
| 156 | [x] | `getGifs` | 6456–6495 |
| 157 | [x] | `getScheduledAttachment` | 6496–6566 |
| 158 | [x] | `getUserCheckInOutStatus` | 6567–6596 |
| 159 | [x] | `getAllScheduledMessages` | 6597–6615 |
| 160 | [x] | `scheduleTextMessage` | 6616–6670 |
| 161 | [x] | `scheduleAttachment` | 6671–6765 |
| 162 | [x] | `scheduleGiphyMessage` | 6766–6815 |
| 163 | [x] | `rescheduleAllMessages` | 6816–6852 |
| 164 | [x] | `reScheduleMessage` | 6853–6889 |
| 165 | [x] | `editScheduleMessage` | 6890–6934 |
| 166 | [x] | `deleteScheduledMessage` | 6935–6953 |
| 167 | [x] | `deleteAllScheduledMessage` | 6954–6973 |
| 168 | [x] | `sendScheduledMessage` | 6974–6992 |
| 169 | [x] | `sendAllScheduledMessage` | 6993–7058 |
| 170 | [x] | `performCheckInOrCheckOut` | 7739–7798 |
| 171 | [x] | `getUserRemoteData` | 7799–7868 |
| 172 | [x] | `getSlashCommands` | 7869–7924 |
| 173 | [x] | `getSlashCommandsSuggestions` | 7925–8237 |
