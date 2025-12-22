# Stats Folder

The **Stats Folder** type displays world and instance statistics on button displays. It integrates with the World Stats asset to query metrics from the VRChat API and show them on the Launchpad interface.

## Overview

Stats Folder transforms buttons into information displays showing real-time world data. Perfect for:

- Player count display
- Instance information
- World visit statistics
- Performance metrics
- Server region display
- Time-based information
- Custom analytics display

## Configuration

### Prerequisites

The Stats Folder integrates with the **World Stats** asset (created by the same author):

- World Stats asset must be installed
- World Stats component must be in the scene
- VRChat API integration must be configured
- Appropriate permissions set for data access

### Basic Setup

1. **Install World Stats Asset**:
   - Obtain and import the World Stats asset
   - Add World Stats prefab or component to scene
   - Configure API access and permissions

2. **Create a Stats Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Stats Folder"**
   - Name your folder (e.g., "World Info", "Statistics", "Analytics")

3. **Configure Stats Component Reference**:
   - Assign the World Stats component reference
   - This links the Launchpad to the stats system
   - Connection enables data flow to displays

4. **Select Statistics to Display**:
   - Choose which metrics appear on which buttons
   - Each button can show a different statistic
   - Configure display format and update rate

5. **Set Display Labels**:
   - Add descriptive labels for each stat
   - Examples: "Players Online", "Total Visits", "Region"
   - Labels appear alongside the stat value

### Available Statistics

Common statistics that can be displayed (depends on World Stats asset configuration):

#### Instance Information
- Current player count
- Instance capacity
- Instance type (Public/Friends/Invite)
- Instance region/server
- Instance master name
- Instance age/uptime

#### World Statistics
- Total world visits
- Total favorites
- World popularity rank
- World heat ranking
- Visit count today/this week

#### Player Information
- Local player name
- Local player rank
- Players in instance (names)
- Friends in instance count

#### Performance Metrics
- Current FPS
- Frame time
- Network ping
- Memory usage (if exposed)

#### Time Information
- Current time (local/UTC)
- Instance duration
- Event countdown timers

## Use Cases

### Instance Info Display

Show current instance details:

```
Stats Folder: "Instance Info"
Displays:
- Button 1: "Players" → Current player count / Max capacity
- Button 2: "Region" → Server region name
- Button 3: "Type" → Instance type (Public/Friends/etc.)
- Button 4: "Master" → Instance master's name
```

### World Analytics

Display world popularity metrics:

```
Stats Folder: "World Stats"
Displays:
- Button 1: "Total Visits" → All-time visit count
- Button 2: "Favorites" → Total favorites count
- Button 3: "Heat" → Current heat ranking
- Button 4: "Today" → Visits today
```

### Event Information

Show event-related data:

```
Stats Folder: "Event Info"
Displays:
- Button 1: "Time" → Current time
- Button 2: "Duration" → Event elapsed time
- Button 3: "Next Set" → Countdown to next performer
- Button 4: "Attendees" → Current player count
```

### Performance Monitor

Display technical metrics:

```
Stats Folder: "Performance"
Displays:
- Button 1: "FPS" → Current frames per second
- Button 2: "Ping" → Network latency
- Button 3: "Uptime" → Instance uptime
- Button 4: "Memory" → Memory usage
```

## Behavior Details

### Display Updates
- **Update Rate**: Configurable refresh interval (e.g., every 5 seconds)
- **Real-Time**: Some stats update in real-time, others periodically
- **API Limits**: VRChat API has rate limits; respect them
- **Local vs. Network**: Some stats are local, others require API calls

### Display Format
- **Text Format**: Customize number formatting, units, labels
- **Dynamic Length**: Text adjusts to fit display
- **Color Coding**: Optional color based on value ranges
- **Icons**: Some displays can include icons

### Networking
- Stats display state syncs across players
- All players see the same data
- Update triggers network events
- Late joiners see current stats

### Performance
- API calls are periodic, not continuous
- Local stats (FPS, ping) are lightweight
- Network stats require bandwidth
- Cache results to minimize API calls

## Tips and Best Practices

### World Stats Integration
- **Follow Documentation**: Refer to World Stats asset documentation
- **API Setup**: Properly configure VRChat API access
- **Rate Limiting**: Respect VRChat API rate limits
- **Error Handling**: Handle cases where stats unavailable

### Display Configuration
- **Update Frequency**: Balance freshness vs. performance/API limits
- **Important Stats First**: Put key metrics on first page
- **Readable Format**: Format numbers clearly (e.g., "1.2K" vs "1234")
- **Labels**: Use clear, concise labels

### Organization
- **Group Related Stats**: Keep similar metrics together
- **Priority Order**: Most important stats on first page
- **Context**: Provide context for what stats mean
- **Units**: Include units where appropriate (%, ms, MB)

### Performance Optimization
- **Reasonable Update Rates**: Don't update every frame
- **Cache Data**: Store and reuse API responses
- **Local First**: Prefer local stats when possible
- **Batch Requests**: Combine multiple stat requests

## Common Issues

### Stats Not Displaying
- Verify World Stats component is assigned
- Check API configuration and permissions
- Ensure World Stats is initialized
- Confirm network connectivity

### Stats Not Updating
- Check update interval configuration
- Verify API rate limits not exceeded
- Ensure World Stats component is active
- Check for script errors in console

### Wrong Data Shown
- Verify stat type selection is correct
- Check data mapping configuration
- Ensure World Stats version compatibility
- Test with single stat first

### Performance Issues
- Reduce update frequency
- Limit number of stats displayed
- Check for excessive API calls
- Optimize text rendering

## Integration with Other Systems

### With Objects Folder
- Show/hide stat displays based on context
- Example: Show stats panel when button pressed

### With Presets Folder
- Stats Folder typically doesn't save to presets
- Display state is real-time, not saved
- May save display configuration

### With Whitelist
- Restrict stat visibility to whitelisted users
- Show different stats to different user groups
- Admin stats vs. public stats

## Examples

### Club Instance Display

```
Stats Folder: "Club Info"
Displays:
- "Current DJ" → Instance master name
- "Crowd Size" → Player count
- "Time" → Current time
- "Region" → Server location
- "Uptime" → Instance duration
- "Visits Today" → Daily visits
```

### World Hub

```
Stats Folder: "World Stats"
Displays:
- "Total Visits" → All-time visits
- "Favorites" → Favorite count
- "Online Now" → Current player count
- "Heat Rank" → Current heat
- "Last Update" → Last stats refresh
```

### Event Dashboard

```
Stats Folder: "Event Dashboard"
Displays:
- "Attendees" → Current players
- "Event Time" → Elapsed time
- "Next Act" → Countdown timer
- "Peak Today" → Max concurrent players
- "Total Today" → Total visitors today
```

### Technical Info

```
Stats Folder: "Tech Info"
Displays:
- "Instance ID" → Instance ID (truncated)
- "Region" → Server region
- "Type" → Instance type
- "Capacity" → Current/Max players
- "Platform Mix" → PC/Quest ratio
```

## Advanced Techniques

### Custom Stat Calculations
- Extend World Stats with custom metrics
- Calculate derived statistics
- Aggregate data over time
- Create custom analytics

### Conditional Display
- Show different stats based on conditions
- Example: Show performance stats if FPS low
- Context-aware stat selection

### Historical Data
- Track and display stat trends
- Show peak values, averages
- Create graphs or charts
- Long-term analytics

### Integration with Events
- Display event-specific information
- Countdown timers to scheduled events
- Performer rotation information
- Set duration and timing

## Limitations

### API Constraints
- VRChat API rate limits apply
- Some data may have access restrictions
- API availability depends on VRChat services
- Not all metrics available via API

### Update Frequency
- Real-time updates not always possible
- Some stats have inherent delay
- Network latency affects update speed
- Balance update rate vs. performance

### Display Space
- Limited space on button displays
- Long text may truncate
- Format data for brevity
- Consider abbreviations

## Next Steps

- [Objects Folder](objects-folder.md) - Show/hide stat displays
- [Presets Folder](presets-folder.md) - While stats don't save, understand preset system
- World Stats Asset Documentation - Detailed stats configuration

---

**Navigation**: [← Skybox Folder](skybox-folder.md) | [Shaders Folder →](shaders-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
